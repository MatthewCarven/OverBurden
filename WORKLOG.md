# MinerCraft — Worklog

## 2026-09-01 — M6d-3: the number row goes where the hand expects it, and the game gets a face

The buildable half of M6d-3. `MinerCraft.Game/Ui/Hud.cs` is a hotbar, a crosshair and a status
line; **1–9 select a material and T cycles the tool tier**, which is the two axes swapping places.
Suite **734 → 739**. **The three play items are Matthew's and are not done** — they are the point
of the part and no amount of code closes them.

### The number row was on the wrong axis, and the argument is four days old

M6d's own TODO named the collision and did not settle it: *"the number keys pick tool tiers today,
so material selection is a second axis."* Both want 1–9, and only one of them is what a hand
expects to find there — Minecraft's number row is the hotbar and it holds **what you build with**.
**This is the same argument that swapped Ctrl and Shift back on 2026-08-31**, decades of muscle
memory against this project's few days, and it was Matthew's call again.

The tiers lost almost nothing by moving: **two picks ship**, so a cycle key on `T` is one press
where the number row was one press. **If a mod ever ships eight picks, that trade inverts** — the
tiers want the row back and the materials want somewhere else to go, and that is the shape of the
decision when it arrives rather than a problem now.

`SelectTier` was polled every frame from `UpdateMining` and is now an event on the key. That is
not tidying: the poll compared against `ReferenceEquals(_mining.Tool, _tiers[i])` to avoid
re-announcing sixty times a second, which is a guard that exists only because the input was being
read in the wrong place.

### A hotbar slot must not move under the player's fingers, and one rule buys that at a price

`VolumetricInventory.MaterialInSlot` is the row: **the carried materials in ascending ID order.**
Three properties, and the third is why it won:

- **The hotbar is what you are carrying, not what exists.** Same argument `NextMaterial` already
  made — a slot offering a material you have none of is a selection that cannot be acted on, and
  with five shipped materials most of the row would be that.
- **The cost is stated rather than hidden: picking up a material you did not have shifts every
  slot above it.** Mine your first dirt and the stone on 1 may become 2 while your hand is on the
  key. Accepted at M6 because the fix — a slot assignment the player arranges — is state that has
  to survive a quit, which is a header field and a format version, in the part of M6 most likely
  to be redone. **Revisit trigger: the play reports a slot moving under their fingers.**
- **ID order beat first-acquired order on the one case that separates them.** Acquisition order is
  stable *within* a session and loses across a quit: `Reset` replays the header's entries and a
  session's acquisition order is written down nowhere, so the row a player learned in one session
  would be a different row in the next. **A rule that is always the same beats one that is usually
  better and occasionally lies.** `TheRowIsUnchangedAcrossASaveAndReload` is the test that fails
  for the rejected alternative and passes for every other test in the group.

**And it gave an existing guarantee a second, louder dependant.** `Entries()` has sorted by ID
since M6c-2 *so that the same inventory always writes the same bytes* — a save-determinism
argument. The hotbar now means that sort is what a number key **means**, and where a shuffled save
is merely unanswerable, a shuffled hotbar moves the player's materials mid-build. Recorded in the
remarks, because removing the sort now breaks two things that look unrelated.

**Four break-recipes, four caught** — dropping the sort (2 tests), dropping the negative-slot
guard (1), off-by-one on the upper bound (4), and a lookup that ignores its own slot argument (3).

### One definition of what stone looks like

The HUD needs a colour per material and the obvious way to get one is a short palette in the HUD,
which is **two definitions of what stone looks like**: they agree the day they are written and
drift the first time either is retuned, and the symptom is a swatch that no longer matches the
block it places. So `VoxelMaterial.LayerColour` is now the shared array the block textures are
speckled from *and* the swatches are filled from. Same argument as `IsCaveAt` and
`GenerateTerrain` sharing one definition of the cave field.

The jitter stayed beside it rather than folded into it — the HUD wants the flat colour and the
block wants the speckle, and they are the same decision only as far as the hue.

### A material's label is derived and not authored, which is the one thing here that is expensive to get wrong

A `displayName` in the material JSON is the obvious answer and it is the costly one: **mods write
against a format the moment it exists**, and a name that can be authored is a name that wants
translating, which is a localisation system this project has no other reason to grow. The
identifier's path is already a human word — the base game ships `stone`, `dirt`, `grass`, `lamp` —
and it exists for every material including a placeholder standing in for a removed mod. **Revisit
trigger: the first material whose ID is not a sensible label, or the first request to run the game
in another language.**

This is the whole of M6d-3 that could have been an expensive mistake. The rest of the UI is
pixels, and TODO's own note is right that it is the part most likely to be redone.

### There was no crosshair, in a game whose two verbs are both "what am I pointing at"

The wireframe target box has been standing in for one since M3d. That works while you are aiming
at a surface and **vanishes the moment you point at sky**, so the player lost the centre of the
screen exactly when they were hunting for it. Two `ColorRect` bars, no binary asset for a plus
sign.

### The screenshot found both defects and the logs could not have

**Headless has no renderer, so a layout error looks identical to a correct layout in every log
this project prints.** The HUD is instantiated in a plain headless run deliberately — that is the
cheapest thing that executes the whole file and catches a script error, and a UI nobody has ever
constructed headlessly is M5c-2's shape of hole one layer out — but it proves nothing about where
anything lands. Two defects, both invisible to it:

- **The crosshair's vertical bar rendered as a line most of the screen tall.** `SetAnchorsPreset`
  writes offsets, the following assignment to `Size` writes them again, and the next layout pass
  recomputes the rect from the anchors — so the assignment survived for one bar and not the other.
  Anchors and offsets are now set directly and never through a preset plus a size.
- **The litres label was clipped off the bottom edge**, because the row reserved 92 px for
  something needing about 90 including its status line.

**And one thing that looked like a third defect and was not.** A pale vertical line at exactly
screen-centre *x* read as a stray wireframe edge; colouring the crosshair red isolated it, and
sampling the pixels gave **(238, 229, 88)** — the yellow cell outline's own back vertical edge,
spanning exactly the box's height, misread as white against green. **Worth recording because the
instinct that found the two real defects also manufactured a third**, and the pixel sample is what
separated them. Eyeballing a screenshot is a measurement too.

### The HUD is told and never asked

Every value it draws is read from the controller that owns it — the selection and brush from
`PlaceController`, the tier from `MiningController`, the litres from `VolumetricInventory`. A HUD
holding its own copy of the selection would be a second answer to *what am I holding*, and the two
would disagree exactly when it mattered: the frame after a material ran out and the controller
re-selected without being asked.

**Not created during a profile run**, because the profile reports draw calls and primitives and
M1d's deferred 2×2×2 mesh-block question turns on that counter — UI quads in a number that means
terrain. Verified: `--profile` reports **1521 viewed / 165 drawn / 1926 jobs at 404 quads**,
unchanged to the digit, and the plain headless run reports the same.

### What is left, and it is the part that matters

**The three play items are not done and cannot be done here.** Dig a tunnel and fly away until it
unloads and come back; quit and reopen; build a room with walls, a floor and a ramp down into it.
M6's whole reason for existing is the first of those, and DESIGN's claim that carving-for-detail
and placing-for-bulk divides cleanly is the third. **A sandbox cannot be judged by the person who
built it out of the report it prints.**


## 2026-09-01 — M6d-2: the worry dissolves, and the cost was somewhere nobody was looking

M6d-2 is the measurement half, and everything it measures already ran. Five questions, five
answers, and **the two that mattered were not the two the plan expected**. Suite unchanged at
**734**; the default headless run is unchanged to the digit — 1521 viewed / 165 drawn / 1926 jobs
at 404 quads — which is the thing an instrumentation pass has to prove before any of its numbers
are worth reading.

### 1. The headline: entering a dug world is not slower than generating it

M6 has carried this since its plan. Half-answered twice — deserialise 21 µs a chunk at M6a, a
SQLite point query 6.0 µs at M6b-1, crossover at 2,505 chunks — and what was missing was the
end-to-end figure that includes the serial lane's ordering against eight generating workers.

```
r4h/2v, standing start (the deterministic arm)      first draw      all settled
generate  --nosave                    rep1 / rep2   3.46 / 3.43     3.81 / 3.80
load      19 stored chunks in the box rep1 / rep2   3.48 / 3.43     3.85 / 3.80

r4h/2v, moving at 6 m/s
generate  --nosave                                  3.82 / 3.80     4.64 / 4.62
load      71 loaded + 44 columns restored           3.84 / 3.82     4.62 / 4.61
```

**Indistinguishable — the load-versus-generate gap is smaller than the rep-to-rep gap.** The
standing start is used because M5d-1 established it as the only arm where the chunk set at a fixed
radius is deterministic, so this compares two numbers rather than two samples of a moving camera.

**The limitation is stated rather than buried: 176 stored chunks is the most this world could be
dug to, against 1,521 tracked, so the 2,505-chunk crossover is confirmed in direction and not in
fact.** The scripted flythrough is a straight line along +X, so repeated passes deepen one trench
rather than covering new ground — five passes went 99 → 104 → 112 → 123 → 176 stored and were
plainly asymptotic. A world dug past the crossover is not reachable with the instruments that
exist, and building one is not M6d-2's job. **Nothing here retires the concern; it moves it from
"predicted to bite at 2,505" to "measured not to bite at 176".**

### 2. The finding nobody was looking for: the box-difference sweep costs more than the pipeline

`ChunkStreamer.MoveViewer` is called from `_Process` **before** the bracket that produces
`update ms`, so **every profile from M2d to M6d-1 measured the pipeline and not the sweep.**
Timed for the first time today:

```
                                     tracked   viewer ms mean / p95 / max    update ms mean
r4h/2v   6 m/s  no world open           1521    7.09–9.02 / 13.4–14.7 / 15.3      2.34
r4h/2v   6 m/s  world open              1521    9.52–10.0 / 14.6–15.5 / 15.6      2.33
r6h/3v   6 m/s  no world open           3179   13.21      / 19.32     / 19.61     4.05
r4h/2v  40 m/s  world open + mining     1521    4.84      /  6.55     / 13.69     5.31
```

**Against a 16.7 ms frame, and two to four times the whole pipeline update.** It fires only when
the viewer crosses a chunk boundary — 15 times in a 20 s run at 6 m/s — so it is a spike rather
than a load, which is exactly the shape `SampleSeries`' own doc comment says a mean hides.

Two consequences worth writing down:

- **`main %` understates the main thread and always has.** It is computed from `PipelineUpdate`
  alone. At r6h/3v the report says 31% of one core while the sweep is spending another 13.2 ms on
  15 frames out of ~1,200.
- **It is sub-linear in radius**: 2.09× the tracked chunks for 1.47× the sweep. So it is not
  purely per-chunk work.

**And it is not persistence**, which is the half M6 was actually worried about — see below.

### 3. `Forget` becoming a write is a non-event, and the design answer is why

M6-PLAN's headline structural risk: *"`ChunkStreamer.Forget` becomes a write … called
synchronously, on the sim thread, from inside `ChunkBox.ForEachDifference`, which at r16 hands it
a whole 37×37 shell in one frame."* The answer was to make the write a job, leaving the sweep a
flag and a list append. Measured on both threads, which is the only way the claim is checkable:

```
dig 60 s @  6 m/s   97–101 chunks + 62–74 columns   34–35 batches, largest 6
                    sim 2.4–3.6 ms total (0.07–0.11 ms/batch) | worker 22.6–25.9 ms (0.66–0.74)
dig 15 s @ 40 m/s   127 chunks   + 76 columns       82 batches, largest 5
                    sim 2.829 ms total (0.0345 ms/batch)      | worker 37.4 ms (0.46)
```

**The sim thread spends single-digit milliseconds on saving across an entire sixty-second digging
run.** The two numbers are printed side by side and deliberately never summed: one is the sweep's
thread and one is a worker's, and the whole claim being checked is that the second is large
*because* the first is not. If they ever converge, the write has come back onto the sim thread and
no other line in the report would change.

**The burst the plan feared never forms, and the mechanism is `SubmitSaves` running every frame
with one batch in flight.** The largest batch across every run measured is **6** — not a 37×37
shell — because the sweep hands over a few dirty chunks and the next frame takes the next few.
That "one at a time so batches grow rather than multiply" comment was written as a transaction
argument; it turns out to be the burst bound as well.

**The control is the load of it.** `viewer ms` with **no world open at all** is 7.09–9.02 mean
against 9.52–10.0 with one open — overlapping. So the sweep's cost predates M6 and belongs to
something else entirely; retaining and releasing light grids and sky columns is the obvious
suspect and is deliberately not chased here. **Measurement, not repair.**

### 4. Precision placement: M6d-1's conclusion was read off the wrong statistic

M6d-1 recorded *"precision costs about twice as much per click while doing a sixty-fourth of the
writes"* and hypothesised the clone-and-relight marking as the cause, explicitly leaving it to
M6d-2. **The mechanism is confirmed and the conclusion is not.** Seven reps of each arm, standing
start, r4h/2v:

```
          mean          p50           p95           max          clones  remesh  relight  placed
brush   0.016–0.028   0.013–0.030   0.029–0.045   0.056–0.129     0.00    0.05    0.00      7.73
precise 0.027–0.058   0.004–0.007   0.059–0.103   0.883–1.790     0.19    0.20    0.06      0.40
```

**Precision's median click is 0.004–0.007 ms against the brush's 0.013–0.030 — two to four times
cheaper, exactly as one voxel against sixty-four predicts.** The mean is twice the brush's because
the maximum is **0.88–1.79 ms against the brush's 0.056–0.129**: precision's worst click is
200–300× its own median, the brush's is 2–5× its. One or two outliers in an 88-sample run move the
mean by the entire reported difference, which is why two reps disagreed at 0.058 and 0.031 before
the extra five were run.

**So "twice as much per click" is true of the mean and false of the click**, and this project's own
`SampleSeries` doc comment says why in as many words: *"a mean hides exactly the thing worth
measuring."* Fifth outing for that warning and the first time it caught a conclusion already
written into TODO.

**The hypothesis's mechanism is exactly right and is what the tail is made of.** Precision pays
**0.19 chunk clones, 0.20 edit remeshes and 0.06 relight remeshes per click while placing 0.40
voxels** — about half a chunk clone per voxel placed. The brush places 7.73 voxels a click for
**0.00 clones and no relights at all**, because its voxels land in one already-cloned chunk and its
56 refused voxels never reach `SetVoxel` — M3c's air-skip and M3a's already-there early-out, both
still doing their job one verb over. **M3c's argument for brushes staying the default survives, but
on the amortised-clone ground rather than on the per-click one it was restated on.**

### 5. `Compact()`, compression and what a session actually weighs

```
arm                  write µs   read µs   raw B   stored B   ratio   B/varied
carved, compact         273.0      21.2    3170        124    25.6        189
carved, verbatim         19.8      22.2    3170        124    25.6        189
stripped, compact       112.2       6.8    1323         60    22.0        216
stripped, verbatim       11.4      11.9    2014         65    31.0        148

Compact on a carved chunk   costs 253.2 µs (1,276% of the write) and saves 0.0%
Compact on a stripped chunk costs 100.8 µs (  888% of the write) and saves 7.5%
```

**M6a's decision to leave `Compact` off stands, re-measured rather than re-asserted.**
Compression is 22–31×, comfortably clear of the recorded ~3× revisit trigger at which the CPU
stops being worth the opacity.

**Save size after a real session, which the plan predicted at ~1 KB a chunk and a few hundred KB:**
the world Matthew has been playing is **36 KiB**; the four-pass dug world here is **96 KiB for 176
stored chunks and 80 columns**, at **480–515 B per chunk blob** in the game against the bench's 189
B per varied chunk. **The plan was pessimistic by about 2×, in the safe direction.** The gap
between 189 and ~500 is that the game stores whole edited chunks including their sky columns, where
the bench's figure is the marginal cost of variedness alone — two different quantities, and quoting
either as the other is the denominator error this project keeps recording.

### What was built, because measurement needed instruments

Four counters on `ChunkPipeline` — `SaveBatches`, `LargestSaveBatch`, `SaveQueueMillis`,
`SaveWriteMillis` — plus `SaveWorldJob.ElapsedMillis`, a `ViewerMove` series on the profiler, and
`PersistenceLines()` in the report. **`SaveQueueMillis` is timed after `QueueSave`'s early-out on
purpose**: the overwhelming majority of an unloading shell is untouched terrain that leaves through
that line, and folding it in would price the burst at the cost of not saving.

**One reporting defect found by being misled by it.** `PlacementLines` printed per-click voxel
counts at `F0`, so an arm placing 0.40 voxels a click rendered as `0 placed` — which reads as *the
arm did nothing* for an arm that was working, and cost a whole reading of these numbers before it
was noticed. Now `F2`. Same family as every counter here printing its own denominator, and the
first time that habit's absence has actively produced a wrong reading rather than an unreadable
one.

**And one fixture trap, caught by the numbers refusing to make sense.** The placement arms were
first run at 6 m/s, where they placed *nothing at all*: `--place` aims along the look direction —
deliberately, so the brush does not land in the cell the player is standing in — and the flythrough
holds the camera 4 m above the surface looking level, at sky. A moving placement profile is not a
thing this instrument can produce, and the standing start is the arm. **Zero placements is a loud
failure; the quiet version was the standing-start run before it, where both arms spent most of
their clicks refusing into a cell they had already filled.**


## 2026-08-31 — Ctrl and Shift had been swapped since M2, and sneak is a rule not a key

Raised in conversation: *"I think I may have suggested something wrong, and would prefer to
maintain accuracy to minecraft like, ctrl was crouch."* The key is **Shift**, not Ctrl —
Minecraft Java is Sneak = Left Shift, Sprint = Left Ctrl, and in creative flight Shift descends
while Ctrl flies faster. But the instinct was right and pointed at something real:

| | MinerCraft, M2 to M6d-1 | Minecraft |
|---|---|---|
| Sprint | **Shift** | Ctrl |
| Descend while flying | **Ctrl** | Shift |

**The two were exactly swapped, and had been since M2.** Muscle memory for these keys is decades
old against this project's one day, so accuracy wins. Suite **727 → 734**.

It also explains a note that had been open since M3d from the other direction: Ctrl-as-descend is
what made precision mining sink the builder ~1.8 m between chips. **M6d-1 moved precision to Alt
to dodge that; the swap removes the cause.** Both are still worth having — Alt now means
precision on either mouse button, and Ctrl means sprint, which would have been a worse pairing
for fine work than crawl.

### Sneak is a physics rule, and that is the half that is not a keybinding

While grounded and not rising, a horizontal move that would leave the body unsupported is refused
— **per axis**, so walking diagonally at a drop carries you along the lip instead of stopping
you dead. In a game about standing on ledges over shafts you dug yourself, that is the difference
between building at the edge and falling down your own mine.

**"Supported" is step-down's own probe, called the same way, and the shared definition is the
load-bearing part.** Step-down catches a body that walks off a lip up to the step height; if sneak
asked a stricter question it would refuse moves step-down would have caught, and the player would
be stopped at the top of a staircase they could safely have walked down — which is exactly the
frustration sneak exists to prevent. One definition of *the ground will catch me*, for the reason
`WorldGenerator` keeps one definition of the cave field.

Sneak beats sprint when both are held, which is not merely convention here: sprinting off a ledge
you were carefully sneaking along is the accident the whole rule exists to prevent.

### The battery found two holes, and both were the test finishing before its own case

Five recipes, three caught immediately.

- **"sneak applies in mid-air too" was not caught**, and the obvious test cannot catch it: the rule
  only ever trims *horizontal* movement, so removing the grounded guard does not stop a fall.
  **What the guards actually protect is air control** — the launch frame is grounded but rising,
  every frame after is airborne, so an unguarded rule zeroes the horizontal component of a jump and
  the body goes straight up and lands back on the lip. The test is now that property directly.
- **"only the combined move is tested, never per axis" was not caught**, because the slide test
  walked one metre of Z and reached the lip at the same moment — it finished its travel before
  reaching the case it was named for. Now it runs long enough that most of the sliding must happen
  after the edge.

### Two fixtures that were wrong about the physics rather than the reverse

Worth recording because both are behaviours somebody will otherwise file as bugs:

- **A sneaking body hangs most of itself over the drop.** Support is "some voxel under the
  footprint is solid", so the body walks out until its *minimum* corner reaches the edge. The first
  assertion checked the far face and failed at 2.49 m; the invariant is about the near one. That is
  Minecraft's behaviour too, and it is what makes sneak useful for building a wall from outside it.
- **A gap exactly the body's width is walked across while sneaking, no jump needed**, because one
  end is always over solid ground. A jump test built on a 0.5 m gap therefore tested nothing.

**Three fixture failures this session have now had the same shape: the scenario ended before it
reached the case the test is named after.** Different from a scenario where both arms agree —
this one never gets there at all.

## 2026-08-31 — M6d-1: one voxel at a time, and one key that means precision

`Place.Voxel` is the mirror of `Carve.Voxel`, and **Alt is now the precision modifier for both
verbs**: Alt+left takes one voxel, Alt+right places one. Suite **724 → 727**.

### Why this is M6d's first part rather than its last

M6d's own play item asks *"whether four shapes are the right four"*, and M6c's play answered it
early: cube stairs cannot be walked up — a riser is a metre against M4b's half-metre step —
and the ramp brush buys exactly one metre of rise, so a flight of stairs means aiming at the top
face of the last ramp, a metre at a time. **Judging feel on a verb that is knowingly incomplete
spends the play re-discovering something already written down.** So the fifth shape came first,
and the measurement and the play now happen once, over a finished tool.

### The key swap closes a note recorded at M3d

Ctrl+left has been precision mining since M3c, and TODO has said since M3d that **Alt is the
better key** — Ctrl is also FlyCam's descend, so precision mining while flying sank the builder
about 1.8 m between chips, and Alt is *crawl* in fly mode, so "slow down to do fine work" pairs
with the modifier rather than fighting it. It was left *"pending a play-test"* for two
milestones. **M6c's play was that play-test, arriving from the other verb**, and the swap now buys
a third thing neither note anticipated: one modifier meaning precision across mining *and*
building, learned once rather than per verb.

### The number, and it is the opposite way round

```
--place         place ms  mean 0.040  max 0.177     23 voxels placed per click
--preciseplace  place ms  mean 0.077  max 1.101      1 voxel  placed per click
```

**Precision costs about twice as much per click while doing a sixty-fourth of the writes.** So it
is inefficient in a second way beyond the click count, which strengthens rather than weakens
M3c's argument for keeping the brushes as the default: a wall built voxel by voxel is 64× the
clicks *and* each click is dearer.

> **Corrected 2026-09-01 by M6d-2.** These are *means*, and over seven reps precision's **median**
> click is 0.004-0.007 ms against the brush's 0.013-0.030 — two to four times *cheaper*, as the
> voxel count predicts. The mean is a tail: precision's worst click runs 200-300x its own median
> against the brush's 2-5x. **The mechanism guessed at below is right** and is what the tail is
> made of; the direction of the conclusion is not.

**The cause is inferred and not measured**, which is M6d-2's to settle: the per-placement cost is
presumably dominated by the raycast and by the edit's clone-and-relight marking rather than by the
writes, so one voxel dropped into fresh air pays a full chunk clone for a single change while
sixty-four in one cell amortise it. Written down as a hypothesis with the number beside it.

### The wire, tested rather than assumed

Every layer of precision placement has a Core test and **the join between the held key and
`Place.Voxel` has none** — which is exactly M5c-2's shape of hole, where replacing a gather with
`null` left 556 tests green. So `--preciseplace` exists: it drives the modifier headlessly, and
the run above reports *1 placed / 0 occupied* per click against the brush arm's *23 / 41*. The
report also names the shape actually used rather than the brush selected, because a precise run
under a label reading "cube brush" is M3d's chips-sharing-the-swing-line trap repeated.

### And the outline follows the key

`VoxelHighlight` takes its size per show rather than per instance, so the placement outline is one
metre normally and one voxel while Alt is held — one outline changing scale rather than a third
RenderingServer instance. Without it the player cannot see what precision would do until after
they have done it.

## 2026-08-31 — A saved flying player came back at the origin, underground

Reported as *"after I hit the b key it went into fly no clipping mode"*, and B was not it — B
cycles the brush and returns before the mode key. Reading the played world's header settled it:

```
PLAYER: feet (0.000, -1.650, 0.000), flying = 1
```

Fly mode at the world origin, 1.65 m below zero, which is inside the ground; the noclip is fly
mode doing what fly mode does. **And it is self-perpetuating** — the next launch restores that
position, and the next quit writes it back.

**The mode change was probably the F key** (*"maybe i hit f"*). Nothing explains the *position*,
and that is the bug: he was flying above a structure and the file recorded the scene origin.

### Two bugs in one method, and both are the same mistake

`SavedPlayer()` asked the live camera for the eye position, because in fly mode the camera is
where the player is. That is true during a frame and **not true during teardown**, which is when
`_ExitTree` calls it. The walking half never had the problem, because it reads
`PlayerController.FeetCentre` — plain C# state that does not care whether a node is still in a
tree.

**The rule: state that has to survive a quit must be captured while the frame is alive.** The eye
is now cached every `_Process` while flying, and the save reads the cache. If no frame has run in
fly mode there is nothing honest to say, so the previous answer stands rather than an invented one.

The second bug was next to it and would have bitten later. `CurrentHeader()` is written at open,
before the body exists — so `SavedPlayer()` returned null and **the saved position was cleared
on every launch**, staying cleared until the first autosave. A crash in that window lost it. It
now falls back to what the world was opened with. Verified by a run that quits at frame 2, before
the body spawns: the position survives, where it used to be wiped.

### And the diagnosis is now printed

Quitting says what it wrote — `M6c: saved the player at <15, 1.2501, 15>, walking`. A wrong
position is invisible until the next launch puts you somewhere strange, and the log was the only
place that could have said so earlier. Same instrument as the scheduler flake's shutdown report.

## 2026-08-31 — M6c is played, and the brush set is the wrong granularity for stairs

*"Nah doesn't auto climb but later when we place blocks one at a time this will work better."*

Built with cube brushes, and **a cube riser is 1 m against M4b's 0.5 m step height** — so it
cannot climb, by arithmetic rather than by defect. The ramp brush is the walkable one and
`APlacedRampIsWalkedUpByThePhysicsThatAlreadyExists` still holds.

**But the verdict is right about the thing that matters, and the test does not catch it.** That
test builds two ramps in cells one apart *and one higher*, which is what a staircase actually is
— and a player has to produce that arrangement by aiming at the top face of the previous ramp,
a metre at a time. One ramp buys one metre of rise. Nobody is going to build a flight of stairs
that way, so the brush that passes its test is still the wrong tool for the job the test is named
after.

**The gap is single-voxel placement, and it is a shape rather than a mechanism.** Mining has had
two shapes since M3c — a sphere on the button and one voxel on a held modifier — and the
argument recorded for the precision one is *"for the person who has stopped excavating and started
building"*. Building then shipped with only the bulk tool. `Place.Voxel` is the mirror of
`Carve.Voxel`: `Put` already holds the gate, the buckets and the inventory debit, so the whole of
it is a call site and a key.

M3c's argument about which is the default transfers exactly and is worth restating before it is
built: a voxel is 1/64th of a cube, so a wall built voxel by voxel is 64x the clicks. Precision
stays the exception, and the exception is worth a held key.

**One thing this play did not test, and recording that is the point.** The lattice snap drew no
complaint — but the objection was about brush *size*, so the snap was never really under test.
That is silence rather than evidence, the M5d light-cell shape again, and its revisit trigger
stays live for M6d.

## 2026-08-31 — M6c-3: the binding, and building is a thing you can do

Right mouse places, B cycles the brush, V cycles the material, the inventory fills as you mine
and empties as you build, and all three survive a quit. `PlaceController` is Godot-free for the
reason `MiningController` is, so `--place` drives a whole profile window headlessly. Suite
**719 → 724**; the five are the ramp-facing rule, which moved into Core on the way past.

### What a brush costs

```
place ms  mean 0.038  p50 0.015  p95 0.110  p99 0.186  max 0.186   (raycast + brush, sim thread)
building  34 placement(s) with the cube brush, 20 placed / 44 occupied / 0 inside the player
```

Against a 16.7 ms frame, and against mining's 0.086 ms mean for a sphere half the size. **Do not
build anything to make placement cheaper.** Streaming is untouched: 1521 viewed / 165 drawn /
1926 jobs at 404 quads, which is M2d's line that M5c-1, M5d and M6b-3 have each had to preserve.

### The round trip, end to end through the real game

```
run 1  --world=build --profile --speed=0 --place --stock=2000
       24 placement(s), 680 voxel(s) placed
run 2  --world=build
       M6: opened ... 4 material(s) carried, no saved player
       M6c: 114375 L across 4 material(s)          <- exactly what run 1 was left holding
       M6: 1 loaded / 1521 tracked, 1 columns restored
run 3  --world=build
       M6: opened ... 4 material(s) carried, player restored
       M4c: walk mode, spawn feet 4.00 m, restored from the world file
```

Three things fall out of that. The inventory survives to the litre. **A profile run saves no
player**, because the body never spawns in fly mode — which is right: a measurement should not
move where you live. And run 2 spawned at **4.00 m** rather than the usual 1.25 m, because run 1
had built a cube where the spawn column is — the structure came back and the player stood on it,
which nothing was asserting and is the most convincing evidence in the set.

### Both outlines, and why there are two

A brush lands on the 1 m lattice wherever inside a cell you pointed. Without an outline of the
cell itself that snap reads as the game ignoring the aim — which is exactly what M6d has to judge,
and it cannot be judged if it is invisible. So `VoxelHighlight` gained a size and a colour, and
there are two instances: the 0.25 m dark box around the voxel a swing would take, and a 1 m yellow
box around the metre a brush would fill. In a screenshot they nest, and read as *this voxel / this
metre*.

**Headless cannot answer that question** — no renderer, so a highlight that scaled wrongly or
never showed would look identical in the logs. A screenshot is the only instrument, which is the
M5c-2 lesson about shader errors not surfacing headlessly, one layer out.

### Three decisions the binding forced

- **The body is passed while walking and not while flying.** In fly mode the body is inert and
  sits wherever the last F left it, so passing it would refuse voxels around a position the player
  is no longer at — a placement failing for a reason that is nowhere near the crosshair.
- **The material selects itself when nothing is selected**, taking the largest holding, so that
  mining a first bite and then reaching for the other mouse button works with no key press in
  between. Cycling stays the player's. −1 means "carrying nothing" rather than air, because air is
  a legal ID that `Place` refuses, and using it as the empty sentinel would turn an empty
  inventory into an exception rather than a no-op.
- **`Place.FacingFrom` moved into Core** beside `TargetCell`, on M3d's grounds: which way a ramp
  rises is a game rule a server would have to agree about, exactly as where the brush lands is. It
  rises **away** from whoever placed it, so walking forward onto it climbs it — both readings of
  "facing" are natural and the wrong one builds every staircase backwards.

### Two defects the wiring found

- **`ChunkPipeline.WriteWorldHeader` was a second sim-thread caller of the store with no drain.**
  M6b-3 gave `SaveAll` one and left this alone, correctly, because at that point the only call was
  at open — before a single job existed. M6c-3 puts it on the autosave timer, where a chunk load
  is very likely to be on the lane. It now drains like `SaveAll` and returns whether it wrote.
  **Third time this milestone that a safety argument turned out to be about the caller rather than
  the code.**
- **The cell outline leaked its RID at exit**, which Godot reports as *"1 RID allocation of type
  'InstanceE' was leaked"* and does not say which one. `_ExitTree` disposed the first highlight
  and knew nothing about the second.

### And a self-inflicted one worth recording

Four C# string literals ended up with real newlines in them because a `\n` written inside a bash
heredoc arrived at Python already unescaped. The memory note says *any heredoc breaks on a long
payload — write the script to a file, then run it*, and this session had been getting away with
heredocs for short patches all day. **The failure is not length, it is escapes**: a payload
containing backslashes is the one that breaks, at any size. Write it with the file tool.

## 2026-08-31 — M6c-2: the inventory, and a format bump that did not strand the world

`MinerCraft.Core/Inventory/VolumetricInventory` holds what the player is carrying; `Carve`
credits it per material as it removes, `Place` debits it per voxel as it fills, and the world
header carries it across a quit along with where the player was standing. Suite **689 → 719**.
Nothing is wired into the game yet — that is M6c-3, the same shape as M6b-3.

### The unit, and why it is stored as something else

A 0.25 m voxel is 15.625 L, so a 1 m brush cube is 64 voxels = **exactly 1,000 litres, exactly one
cubic metre**. That round number is the whole argument for the unit: a player can compare a hole
with a stack without arithmetic.

**It is stored as voxel counts and presented in litres, which is not a contradiction.** A voxel is
the only granularity in which anything can currently enter or leave the world, so counting them is
exact and every litre figure is a presentation of an integer. Litres in a float would drift across
a session, and "a short inventory places what it can" would stop having a clean answer. **Revisit
trigger: the first thing that moves a quantity which is not a whole number of voxels** — M8's
volume-conserving fluids are the likely one, a crafting yield of "0.3 m³" would do it too — at
which point this wants fixed-point litres and the conversion is the whole of the change.

### Where the credit and the debit happen, and both are one line from being wrong

- **The credit is inside `Carve.Take`, per voxel, after the write succeeds.** Not from
  `CarveResult.Removed`, because a bite through a dirt bank with a stone seam in it takes two
  materials and `Removed` is one number — anything crediting from the result would have to guess
  which. Not before the write either: a write that fails must cost nothing.
- **The debit is inside `Place.Put`, after the write, and the stock is consulted *last*.** Charging
  before the voxel is known to be fillable would mean a wall built against a cliff cost the volume
  of the cliff. The bucket order is body → unloaded → occupied → **stock** → write.
- **A short inventory places what it can** and reports the rest as `OutOfStock`. A partial brush
  rather than none: running out halfway through a wall leaves the part you could afford, and the
  bucket itself says what you were short.

### The format bump, and the policy it settles

`WorldHeaderBlob.Version` is 2, which is the change its own remark predicted at M6a. It carries
the inventory (ordered by material ID, so the same holding always writes the same bytes) and an
optional `PlayerSave` — feet position and walk-or-fly, deliberately **not** the facing or the held
tier, neither of which was agreed and both of which cost one more version to add.

**Version 1 is migrated rather than refused, and the rule is worth stating because it looks like
the thing M6a argued against.** M6a's argument was about a reader filling in a field the writer
meant to write — a defaulted `CaveLatticeStep` silently reshapes terrain. Here the change is
strictly additive: a v1 header is a v2 header without its two trailing sections, so reading one
means supplying two defaults that are *true statements* about it — no inventory, no saved player.
The policy: **an additive version migrates, a version that changes what a field means is refused.**

That is not theoretical. Matthew has been playing a v1 world since this morning, and after the
bump it opens, migrates and loads its eight saved chunks:

```
M6: opened .../worlds/default/world.db — seed 0x4D494E455243, 5 material id(s) restored
M6: world written on quit — 8 loaded / 1521 tracked, 8 chunks in the world, 4 columns restored
```

### The batteries, and three tests that were throwing for the wrong reason

Seventeen break-recipes across the two halves. Every one is caught now; four were not at first,
and **three of those failures are the same new variety, which is worth naming: a corruption test
that throws, but from a different check than the one it is named after.**

The world header validates in layers — a length, then a count, then per-row rules, then ordering.
A test that corrupts a byte and asserts `Throws<BlobFormatException>` looks like it covers the rule
in its name, and can in fact be tripping an earlier guard entirely:

- "an inventory holding a material the table lacks" edited **row 0** to an out-of-range ID, which
  also made the rows descending — so the *ordering* check fired, and deleting the range check
  changed nothing. Fixed by raising **row 1** instead, which keeps the rows ascending.
- "a player-present flag that is neither 0 nor 1" was written against a header with **no player**,
  so with the check deleted the reader ran out of bytes and threw anyway. Fixed by writing a header
  *with* a player, so there really are thirteen bytes behind the flag.
- "an inventory longer than the table could explain" was tripping the *length* check rather than
  the count check. Renamed to what it actually covers — and the count's other arm is now recorded
  as **belt and braces with no test, deliberately**: at most one row per material and never air, so
  any count large enough to trip it forces a payload whose rows are duplicated, out of order or out
  of range, and one of those fires first. M4a's zeroable-contact-epsilon category again.

The fourth was in the placement half and is the familiar shape: **the fixture made both arms
agree.** "Stock consulted before the occupied check" changes nothing when there is plenty in hand —
the ordering only matters when the stock runs out *and* some voxels are occupied. The test now
holds **exactly enough for the half that is air**, which is the only fixture that separates them.

**The generalisation: with layered validation, `Assert.Throws` is not evidence that the layer you
meant was reached.** Only a battery says which guard actually fired.

## 2026-08-31 — M6c-1: the brushes, and a ramp you can walk up

`MinerCraft.Core/Building/` holds `BrushCell`, `BrushFacing`, `PlaceResult` and `Place` — cube,
slab, ramp and sphere, on the world's 1 m lattice, writing through `IVoxelEditor.SetVoxel` one
voxel at a time exactly as `Carve` does. Pure Core: no inventory, no Godot, no threads. Suite
**670 → 689**.

### The headline is that the physics already handles it, and it is a test rather than an argument

The staircase-not-slope decision rests on one number: a ramp step rises 0.25 m and M4b's step
height is 0.5 m, so a placed ramp needs no new collision shape and no new anything. That is
arithmetic until a body walks up one — and both halves are pure Core, so it costs a test rather
than a play-test:

```csharp
Place.Ramp(world, Table, new BrushCell(2, 1, 0), Stone, BrushFacing.PositiveX);
Place.Ramp(world, Table, new BrushCell(3, 2, 0), Stone, BrushFacing.PositiveX);
Place.Cube(world, Table, new BrushCell(4, 2, 0), Stone);       // a landing
// ...180 steps of MoveIntent.Walking(East). No jump, ever.
Assert.InRange(state.Box.Min.Y, 3.0f, 3.0f + contact);
```

**It failed the first time, and the failure was the test rather than the ramp**: the body climbed
both flights, strolled off the end of the landing and fell back to the floor, so the assertion
reported feet at 1.0 m and read exactly like "it never climbed at all". A wall at the top fixes
it, and the peak height is now carried into the failure message so the next version of that
mistake says which it was.

Note the second ramp sits one cell higher than the first. That is how a staircase actually tiles,
and it is a second reason the brush set only makes sense on a lattice.

### The break-recipe battery: nine recipes, eight caught, and the hole was a fixture

The one that was not caught: **give `Place` its own private copy of the sphere membership test
with `<` instead of `<=`.** `APlacedSphereExactlyRefillsACarvedOneOfTheSameRadius` exists
precisely to catch a divergent copy, and it did not, because the fixture used an arbitrary centre
and radius — under which **no voxel centre lands at exactly the radius, so `<` and `<=` agree
everywhere**. The scenario made both arms the same answer.

Fixed by choosing a fixture where the boundary case is reachable: centre on a voxel centre, radius
a whole number of voxels, so the voxels along each axis sit exactly on the surface. The fixture
now **asserts that it reached that case** — the voxel exactly a radius away must be in the carved
set — so if it ever stops being reachable, the fixture says so instead of going quiet.

Worth stating separately, because it is a rule and not a one-off: **mutating the shared
`VoxelSphere` is invisible to that test by construction.** Both sides move together and still
agree. The only recipe that means anything against a shared definition is the one that
*un*-shares it. That is the writer/reader-pair lesson from M6a in a third shape.

### An invariant that would have been true by construction

`PlaceResult.IsAccounted` — candidates equal the sum of the buckets — is copied from
`CarveResult`, where it is documented as catching "a `continue` added later that is counted
nowhere". The first version of the tally computed `Candidates` as the sum of its own four
counters. Every test would have passed, forever, including the ones asserting the invariant.

So `Tally.Candidate()` is a separate call from `Tally.Add(bucket)`: the shape counts a voxel, then
the gate decides about it, and a `continue` between them fails eight tests. Verified as a recipe
rather than assumed — it is the third entry in the battery.

### What the brushes actually do

- **Only air is replaceable**, and the argument is the hardness gate rather than taste: a brush
  that overwrote material would delete anything without mining it — place dirt into a bedrock
  voxel, mine the dirt with a `hand_pick`, and the bedrock is gone, which makes M3c's gate
  bypassable by the other verb. A non-air voxel is `Occupied` and is not written. **Placing air is
  refused outright** for the same reason, more directly: it is a carve with no tool and no gate.
- **A consequence, and it corrects the plan and a source comment:** placement never reaches
  `SetVoxel`'s "material is already there" early-out, which that comment predicted it would.
  Placement decides its bucket from a read it has to do anyway and never offers the write. So the
  early-out now has **no caller that fires it at all** — M4a's zeroable-contact-epsilon category,
  third outing — and is kept for the reason it was kept last time: it protects the next writer.
- **The lattice is `BrushCell`, deliberately not `LightCellPos`**, which is also 1 m and shares
  this origin. Reusing it would work today and cost nothing, and the 1 m light cell carries a live
  M5 revisit trigger: halving it would silently halve the brush lattice and move where every brush
  in the game lands, from inside a change about lighting. Two things that are the same size for
  unrelated reasons want two constants.
- **The snap is a right shift, never a division.** `VoxelPos` records that truncating division
  puts voxel −1 in chunk 0 beside voxel 0; the same mistake here would put two adjacent metres of
  wall in one brick. Half the world has negative coordinates, and the recipe catches it there
  first.
- **Voxels overlapping the player's box are refused into their own bucket** and the rest of the
  brush still lands. The body is a parameter rather than a lookup, because Core has no player and
  `Aabb.VoxelBounds` already answers the question.

### Two things that moved

- **`VoxelSphere` is extracted from `Carve` into `Core/Voxels`**, so a sphere carved and a sphere
  placed at the same radius are the same set of voxels. Filling in a hole you just made and being
  left with a rind of stragglers is the failure, and it is invisible until somebody tries it. The
  membership fuzz that has covered `Carve` since M3c now covers both callers.
- **`TestWorld` gained the write half** and is now an `IVoxelEditor`, so a placement and a walk
  happen in one world rather than in two doubles that agree with each other. Its older twin,
  `CarveTests.FakeEditor`, is the same idea with a uniform background; merging them is a one-time
  tidy and is on the TODO.

### The scheduler flake now leaves a diagnosis

`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` expired its ten-second wait
a second time, again during a full-suite run under load, and again recorded nothing but the test's
name. Three consecutive clean full runs afterwards, and eight clean runs of that class alone.

Rather than guess, the wait now **shuts the scheduler down and reports what came back**, because
that separates the two candidate causes: `JobScheduler.Dispose` joins its workers, so a survivor
that was merely never scheduled comes back **Completed**, while one whose worker actually died
with the exception is still queued and comes back **Dropped** from the shutdown sweep. The next
occurrence says which. Left as a known flake with its evidence, not as a fixed bug.

## 2026-08-31 — M6b-3: the session, and the game finally saves

`Main.cs` opens a world, seeds the registry from its ID table, hands the store to the pipeline and
writes on a timer and at quit. **M6b-1 and M6b-2 were exercised by tests only; this is the part
that turns a green suite into play that stops losing work.** Suite **668 → 670**.

The verification is a dig, a quit, and a reopen, headless:

```
run 1  --world=verify --profile --speed=0 --mine --entangle --measure=6 --autosave=3
       52 swings, 26 voxels removed per swing
       M6: autosaved 5 chunk(s) and 1 column(s) in 13.4 ms
       M6: autosaved 5 chunk(s) and 0 column(s) in 4.3 ms
       M6: world written on quit — 11 chunks + 1 columns saved, 9 chunks in the world

run 2  --world=verify
       M6: opened .../worlds/verify/world.db — seed 0x4D494E455243, 5 material id(s) restored
       M6: 9 loaded / 1521 tracked, 1 columns restored
       414 quads per drawn chunk    (against a pristine 404)
```

**The excavation comes back in the geometry, not only in a counter.** A longer dig moves `drawn`
too — a twelve-second run took it to 166 against M2d's 165, because a chunk that meshed to nothing
now has a face. And the default run is unchanged to the digit: **1521 viewed / 165 drawn / 1926
jobs** at 404 quads, which is M2d's line that M5c-1 also had to preserve. A world with nothing
saved takes the generate path throughout, so any movement there would have been a regression
rather than persistence doing its job.

### The finding: the store's contract had a second caller, and the loads are the common case

M6b-1 wrote `IWorldStore` as **not thread-safe, as a contract**, and justified it by *every call
arriving on the serial lane*. M6b-2 then added `SaveAll`, which runs on the sim thread, and
justified it by *quitting being exactly when the scheduler stops*. Both are reasonable and
together they are false the moment `SaveAll` is called anywhere else — which is precisely what an
autosave timer is.

The obvious version of the hazard is a save in flight. **The real one is a load**: three job types
touch the store, and `LoadChunkJob` is submitted once per stored chunk entering the box. Walking
back into a region you dug queues one per chunk, so "is the lane inside the store right now?" is
usually *yes* while the player is moving. Against SQLite an overlap is not a stale read — it is two
threads binding parameters on **one shared prepared statement inside one connection**, which is the
file rather than the frame.

`ChunkPipeline` now counts store jobs in flight and `SaveAll` drains the lane before it writes:

- **The counter comes down on every outcome, not only on success.** A dropped load has left the
  store as surely as a completed one, and a counter that only fell on completion would wedge the
  drain for its whole timeout at every quit.
- **The drain terminates because nothing it does submits.** Applying a result can push a chunk onto
  the work list or queue a light build; jobs only ever reach the scheduler from `Pump`,
  `SubmitSaves` and `OnColumnCreated`, none of which runs from there.
- **Its bound is wall clock rather than `_nowTicks`**, which tests inject and which is free never
  to advance. It is a safety valve against a wedged lane, not a budget, and the wait it exists for
  is one job of a few microseconds.
- **`_ExitTree` keeps its existing order — pipeline, then scheduler — and that is now load-bearing
  for a second reason.** Disposing the scheduler first would drop whatever was queued on the lane,
  and `JobResult`'s contract says a dropped job reports Dropped rather than running. For a mesh
  that is nothing; for a save it is a deleted tunnel.

`SaveAllWaitsForTheSerialLaneRatherThanEnteringTheStoreBesideIt` pins it with a store that
**records an overlap rather than crashing on one** — the in-memory dictionaries only sometimes
notice two threads, and "sometimes" is what a race looks like right up until it is a corrupt world
file. Removing the drain makes it fail.

### The second defect: the synchronous save had no fault path

`ApplySave` has restored a failed batch since M6b-2 — pending lists, column dirty bits, index
claims. `SaveAll` had none of it, and it clears the flags **before** the write (deliberately, so an
edit arriving during it is not swallowed). So a store that threw left the world marked saved and
never written, silently, and the next call had nothing left to do.

Reachable only through the synchronous path — which is to say through the autosave timer and
through quit, the two callers M6b-3 adds. Same family as the races M6b-2 had to transfer across on
purpose, one layer further down: **the answer existed, in the twin of the method that needed it.**

### The decisions that were open, and where they landed

- **The world file is `user://worlds/<name>/world.db`.** `user://` is Godot's per-user data
  directory and was never in doubt; the argued part is the extra path segment. A second world — a
  scratch arm, a new game, a backup — is then a directory beside the first rather than a migration
  of a file at the root, and **moving a world file later strands the world in it**. WAL leaves
  `-wal` and `-shm` sidecars next to the database anyway.
- **A measurement or a screenshot prop does not open the played world.** `--profile`, `--fall` and
  `--debuglamp` leave persistence off unless `--world=` names one; `--nosave` turns it off
  outright. Two separate reasons: `--mine` would carve up the world somebody plays, and **a run
  that loads what an earlier one saved is not comparable with M2d's figures**, because the
  streaming shape changes when generates become serial reads. `--world=` is the explicit opt back
  in, which is how M6d will measure the load path.
- **An unopenable world refuses to start.** Loud and fatal, rather than carrying on with
  persistence quietly off — that failure is an hour of digging lost to a line in the log, which is
  the whole thing this milestone is about. Reading a world written by a newer format is the case
  that reaches it.
- **An existing world's `--cavestep` is ignored, with a warning.** The header is what the stored
  chunks were generated against.
- **Autosave is 30 s and synchronous.** Measured against real SQLite: **13.9 ms** for the first
  write of 5 chunks and a column, then **3.6–4.3 ms** for 4–5 chunks. The asynchronous path exists
  and is what an unload uses; a timer that hands work to a queue leaves the crash window it was
  built to close still open.

### What is verified and what is not

- **`_ExitTree` is reached on quit** — it prints, and the print carries the figures from after the
  write rather than before it. `--quit-after` uses `SceneTree::quit()`, which is the same teardown
  a window close takes with `auto_accept_quit` on, so the windowed close was verified by proxy.
  **Closed by hand the same day: *"I closed it and re-ran it and it loaded the last world I was
  in."*** No `NotificationWMCloseRequest` handler is needed, and M6b is done.
- **SQLite loads under Godot's `AssemblyLoadContext`.** The native `e_sqlite3` resolves through the
  generated `.deps.json` with `EnableDynamicLoading`, without a `RuntimeIdentifier`. **An export is
  still unverified** — that was M6b-1's recorded worry and it stays open.
- The five-second drain timeout has **no test**, deliberately: exercising it costs five seconds of
  suite time to prove a branch that only runs when something else is already broken.

### One unreproduced failure, recorded rather than left in scrollback

`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` failed **once**, in a
full-suite run made while a headless Godot run was competing for the machine, and has passed
everything since: two clean full suites and eight consecutive runs of the scheduler suite alone.
Nothing in M6b-3 touches `JobScheduler`.

Its first assertion is a **10 s** wait for a single worker thread, which is a long time to be
starved but not impossible on a saturated box. The alternative reading is that M2a's exactly-once
invariant has a rare hole, and **that would matter a great deal** — which is why this is written
down rather than treated as noise. If it recurs, capture the assertion message: the failing run
recorded only the test name.

## 2026-08-31 — M6b-2: the lifetime, and the milestone's sentence comes true

`ChunkPipeline` takes an `IWorldStore`, an edited chunk that unloads is written, and one that comes
back is read rather than regenerated. **The sentence M6 exists for is now a passing test:** dig a
tunnel, fly far enough that the chunk unloads, come back, and the tunnel is still there. Suite
**650 → 665**.

### The oracle

```csharp
int[] expected = Excavate(pipeline, target);
streamer.MoveViewer(viewer, FarAway);   // far enough that nothing of the old box survives
Assert.False(streamer.IsTracked(target));
streamer.MoveViewer(viewer, target);
AssertSame(expected, Snapshot(pipeline, target));
```

No reference implementation to get wrong, the same category as M4a's sealed room. Its sibling does
the other half a moving viewer cannot reach — tear the whole pipeline down and build a new one on
the same store, which is quitting the game and starting it again.

### The defect the tests found, and where it was matters more than what it was

**`SkyColumn` needed the two-flag split and did not have it.** `StreamedChunk` got
`HasEdits` (permanent — "cannot come from the seed") and `DirtySinceSave` (transient — "does not
match disk") deliberately, argued in the plan, with a comment explaining why one flag cannot carry
both meanings. Columns were then gated on `HasEdits` alone.

`HasEdits` is permanent, so it can never say "already written" — every save would rewrite every
edited column, for the lifetime of the world. `SaveAllIsIdempotentAndCheapWhenNothingIsDirty`
caught it on the second call returning `(0, 1)`.

**The lesson is not the bug, it is the location.** The conflation was reasoned about carefully in
one file and reintroduced one file away, in the half nobody was looking at. That is the third time
this exact shape has appeared — `NeedsRemesh`/`RemeshUrgent` at M5c, `HasEdits`/`DirtySinceSave`
here, and now the column — and the first time it appeared *inside* the change that was fixing it.

### Two tests that were asserting on states lasting less than a frame

Both failed, both were wrong, and both were wrong the same way: `ApplySave` runs inside
`ApplyResults` and `SubmitSaves` runs later in the **same** `Update`, so anything that is briefly
true between them is never observable from outside.

- "A failed save puts its work back on the queue" — by the time the test looks, the retry is
  already in flight. Replaced by the end state that actually matters: **a store that fails and then
  recovers ends up holding the data**, with nothing re-dirtying the chunk in between.
- "The index does not claim a chunk that never reached disk" — same window. And the claim turns out
  to be safe for a reason worth recording; see below.

### Why the optimistic index claim is safe, and it is the serial lane

`SubmitSaves` adds a chunk to the saved-chunk index at **submission**, before the bytes are on
disk. A chunk revived in that window would consult the index, find the claim, and submit a load for
a blob that is not there yet.

It cannot happen, and the reason is structural rather than lucky: **both jobs are on the serial
lane, which runs one at a time in priority order, and `JobKind.Save` (30) outranks `JobKind.Load`
(20).** The write always completes before the read that would observe it.

So `Save`'s priority band is about **correctness**, not only about the starvation argument it was
introduced for. Worth stating plainly, because "saves rank highest so they do not starve" reads
like tuning, and lowering it would look harmless.

### The decisions this part had to make

- **A loaded chunk comes back at `GenerationStage.Features`, not `Terrain`**, so `ApplyFeatures`
  does not run over it twice. **Revisit trigger, and it becomes real the moment the feature stage
  places anything:** a chunk that A's features wrote into, but which was never itself edited, is
  not stored — so it regenerates from seed and loses what A gave it, while A comes back at
  `Features` and never re-runs. Re-running is not the fix either, because a feature re-applied over
  material the player has since mined would undo their edit. The candidates are saving
  feature-affected neighbours, or recording per chunk which features have run, and **neither can be
  chosen against a stage that currently places nothing**.
- **A blob the index claims and the store lacks falls back to generating; a blob that exists and
  will not parse faults.** The difference is whether the world is telling us something. Silently
  regenerating over a corrupt base would replace it with untouched hillside and report nothing,
  which is this milestone's failure mode arriving as a well-meant recovery.
- **A failed save is re-queued, which is the opposite of what a faulted generate does.** M2a's rule
  — a fault is a deterministic bug and retrying turns one exception into a spin — is right when the
  lost work is a mesh that can be rebuilt. A lost save is gone. Retried once per frame, and
  reported every time rather than counted quietly.
- **`Dispose` saves synchronously before it cancels anything.** `JobResult`'s contract is that a
  job still queued at shutdown reports `Dropped`, so a world left to the normal path at quit loses
  whatever had not been picked up. The plan named this as the single most likely way to ship a
  data-loss bug in M6, and it is one line.
- **The save job needs no chunk retention at all**, which was not obvious. A `VoxelChunk` is an
  ordinary object: once the job holds a reference the streamer may forget the record freely.
  What protects a chunk that stays *live* is `VoxelsSharedWithJob`, set before submission, so an
  edit clones rather than mutating the arrays the serialiser is reading — M3a's copy-on-write
  reached by a route it was not written for.
- **The column restore is applied by copying into the live object, never by replacing it.** Every
  `StreamedChunk.Sky` in the column caches that instance, so installing a different one would leave
  already-loaded chunks pointing at the old heights — a shaft lit correctly at the bottom and from
  the pre-dig surface at the top.

### The battery: 19 recipes, 12 caught, and the seven green ones are five different things

Two were **real holes and are now closed**: making either the save job or the load job non-serial
failed nothing, which meant the correctness argument written into `SubmitSaves` was unbacked.
`TheSerialLaneKeepsALoadBehindTheSaveItWouldRace` closes both — a store that parks a writer inside
`Write` while the player returns, which is the revive-during-save window the plan asked for.

One is a **comment that overclaimed, now corrected**. Lowering `JobKind.Save` below `Generate`
failed nothing, and the reason is that it is not a correctness property: when both jobs sit queued
the higher band means the write goes first, but if it did not, the load would read nothing, fall
back to generating, and the chunk would be regenerated **for this session while the save still
lands** — wrong on screen, recoverable on disk. The mutual exclusion is the load-bearing half; the
priority is not, and the comment now says so.

One is **unreachable until the feature stage does something**. A loaded chunk coming back at
`Terrain` rather than `Features` re-runs `ApplyFeatures`, which places nothing, so the two arms
agree exactly. That does not weaken the recorded trigger — it is the trigger, restated as a
mutation nothing can catch yet.

One is a **field that still has no reader**. Not setting `HasEdits` on a loaded chunk breaks
nothing, because every gate in the save path is `DirtySinceSave`. `HasEdits` has now survived from
M3a to M6b-2 documented as *"exactly the set M6 has to persist"* and **has never been read by
anything** — the set that actually decides is the other flag. Kept, because it is one bit and it
states a property the store's bookkeeping does not, but recorded as unread rather than assumed
load-bearing.

One is **provably redundant**. Removing `PendingSaves` from `IsQuiescent` fails nothing, and it
cannot: `SubmitSaves` runs inside `Update`, so a non-empty pending list after an `Update` implies a
save is in flight, which implies `JobsInFlight > 0`. Kept because it states the intent and because
the implication breaks the moment `SubmitSaves` gains an early-out.

And one is **belt and braces against an ordering the serial lane makes rare**, which took two
attempts to establish. Removing `QueueColumnLightBuild` from the restore path fails nothing, and
the obvious test for it — hold the column read, let the chunks load and light themselves against
the pre-dig surface, then release — **cannot be written**, because a column read and a chunk load
are on the same serial lane, so holding one holds the other. The column job is submitted during the
streamer's box sweep and a chunk's load job later in the same `Update`'s pump, so the restore is
already running or already queued ahead by the time any chunk of that column can ask for voxels.

That is M4a's zeroable contact epsilon again, with a twist worth keeping: **the mechanism a test
would have to defeat is the same mechanism that makes the case rare.** The attempt was not wasted —
`ASlowColumnReadHoldsTheChunkLoadsBehindIt` now pins the structural property directly, which is
what makes "restore before derive" an argument rather than an observation about timing.

### A fixture trap that cost four failures

`SomeVoxelIn` reached for voxel (16, 16, 16), which in this generator is **often already air** — and
`SetVoxel` correctly returns early when the material is already there, so four tests were making no
edit at all and failing on the fixture rather than the code.

Fixed by placing a **lamp**, which nothing generates, and by having the fixture itself assert that
the chunk came out dirty. That assertion is the part worth keeping: without it the next fixture
that stops editing anything would make its tests pass vacuously rather than fail loudly. Same
family as M6a's `Compact` measurement, where the carved-chunk arm could not benefit and said
"saves 0" anyway.

### An incidental confirmation, a milestone early

Placing an opaque block **above** the surface closes the sky column down to it and marks the column
edited — so a built roof persists its lighting exactly as a dug shaft does. That is M6c's placement
case arriving early, and it fell out of a failing assertion rather than being designed for.

### Still outstanding: M6b-3, the session

Nothing in `MinerCraft.Game` is wired yet. The startup reorder — open world, read the ID table,
`Load(source, savedIds)`, freeze, then construct the streamer — and the autosave timer and the quit
drain are M6b-3. **`Main.cs` still calls `Content.Load` with no saved table and still hardcodes the
seed**, so the game as it runs today saves nothing: all of the above is exercised by tests only.
## 2026-08-31 — M6b-1: the store, and M6's headline worry dissolves backwards

`IWorldStore` and `InMemoryWorldStore` in Core; `MinerCraft.Persistence` and `SqliteWorldStore`
beside it. Nothing is wired into the pipeline yet — a chunk still does not save. Suite
**614 → 650**.

**M6b is split**, on the grounds M2c and M5c were: M6b-1 is the store, verifiable on its own with
no threads and no pipeline; **M6b-2 is the lifetime** — the save job, the flags, the load path, the
column restore and the four races; **M6b-3 is the session** — the startup reorder, autosave and the
quit drain. Splitting up front rather than halfway through is the whole reason M2c went smoothly.

### The number the plan was most worried about, measured — and it is the other way round

M6-PLAN's crossover table is the milestone's headline concern: loads run **one at a time** on the
serial lane while generation runs on eight workers, so *"a world the player has dug into heavily
can be slower to enter than one generated from scratch"*. It assumed 0.1–0.3 ms a load and put the
crossover at 300–890 of the 1,521-chunk fill. `dotnet run --project MinerCraft.Bench -c Release --
--store`, over a world where **every** chunk has been edited:

```
file on disk                     248 KiB  (167 B a chunk)
write 1521 chunks             13.4 ms

open the world                  0.74 ms
load the chunk index            0.85 ms   (1521 keys)
read one blob                    6.0 µs
read + deserialise one          35.5 µs

Loading all 1521 on one serial lane: 54 ms, against ~89 ms to generate the same box on
eight workers. Crossover is 2505 chunks, past the whole 1521-chunk fill.
```

**A SQLite point query is 6 µs, not the 100–300 the plan guessed** — a prepared statement against
a page already in cache is barely more than a hash lookup. So loading a fully dug-in world is
**1.6× faster than generating it**, one serial lane against eight workers, and the crossover sits
past the entire fill.

Two consequences, and the second is the one to hold onto:

- **Batched loads are retired rather than deferred.** M6-PLAN named them as the lever and said
  *"do not build it before the number says so"*. The number says do not build it. Same for
  parallel reads under WAL, which was already the worse option.
- **This is the second fallback dissolved in the *wrong direction*** — M5c-2's was the first, where
  light in the merge key turned out to make quads cheaper rather than dearer. The plan did not
  merely overestimate a cost; it had the sign of the comparison wrong. Worth noting because the
  project's standing rule is "measure before building the named fallback", and the reason that rule
  keeps paying is not that estimates are pessimistic — it is that they are **guesses about the
  wrong quantity**. Here the guessed quantity was per-load latency; the thing that actually decided
  it was that a prepared statement amortises to nothing.

### Renamed from the plan: `IWorldStore`, not `IChunkStore`

The plan called it `IChunkStore`, which was accurate until it turned out to own three things —
chunk blobs, sky columns, and the world header carrying the ID table. A store that holds the
material table is not a chunk store. One-line correction, made now rather than lived with.

### The interface decisions, and the one that is a correctness requirement

- **It deals in bytes, not in `VoxelChunk`s.** Storage and format stay separable: `ChunkBlob` owns
  what the bytes mean, and `SqliteWorldStore` knows nothing about voxels, palettes or light. That
  is what lets the SQLite implementation live in its own assembly at all.
- **Chunks and columns are written in one call, in one transaction, and that is not tidiness.** A
  shaft's chunks stored without the column recording how far the sky now reaches down it reload
  with the column seeded analytically — from the *pre-dig* surface — so every cell in the shaft
  fails `IsCellOpenToSky` and the shaft is dark for a reason nothing on screen can explain.
  **Two separate calls would make that state reachable by a crash between them.**
- **The header is written at open, before any chunk, and that ordering replaces a transaction.**
  M6-PLAN asked for the ID table to commit *"in the same transaction as the blob that first used
  it"*. It does not need to: numeric IDs are only ever allocated during content load, which happens
  once at open, so by the time any chunk is written the table it references is already on disk.
  Ordering delivers the same guarantee for free.
- **Not thread-safe, as a contract rather than an omission.** One connection, prepared statements
  held for the store's lifetime and reused. Every call arrives on M2a's serial lane, which runs one
  at a time — and that reuse is most of why a read is 6 µs rather than dominated by statement
  preparation.

### The contract suite, and why the fake gets tested against the real

`InMemoryWorldStore` exists so M6b-2's pipeline tests need no disk. **That is exactly what makes it
dangerous.** M1 built `InMemoryContentSource` for the same reason and it drifted — M3c found it had
no grass, so a pile of content tests had been passing against a world the game does not ship, and
nothing said so until `ShippedContentTests` was written to read the real files.

So there is one abstract `WorldStoreContractTests` and both implementations run it. A behaviour
only the fake has is a fake that has stopped predicting the real thing, and the tests it would
quietly invalidate are the ones standing between a defect and permanent data loss.

Two details in it worth keeping:

- **`AStoredBlobIsNotAliasedToTheCallersArray`** is the specific way an in-memory store diverges. A
  fake that handed back the caller's own array would pass every round-trip test while letting a
  later edit silently rewrite history.
- **`SurvivesAReopen` says out loud that the in-memory store cannot.** A skipped test that reads as
  coverage is what this suite exists to avoid, so the base case asserts the store is not a durable
  one rather than quietly passing.

Also pinned: **negative coordinates are distinct keys.** Half the world has them, and a key built
by packing three ints with a bad shift collapses one chunk onto another — the symptom being terrain
appearing where it does not belong, a long way from the cause.

### Two things the build tripped over, both worth knowing

- **`Microsoft.Data.Sqlite` is pinned at 10.0.11 rather than floating.** The 10.x line resolves by
  default against a net8.0 target and works, but a save format's dependency is not somewhere to
  take whatever the feed offers that day.
- **An XML comment cannot contain `--`**, so documenting the `--store` flag inside a `.csproj`
  comment is an MSBuild parse error rather than a warning. Trivial, and it costs a confusing
  build failure the first time.

## 2026-08-31 — M6a done: the blob, the world file, and a comment that had been wrong since M1

`MinerCraft.Core/Persistence/` holds `ChunkBlob`, `SkyColumnBlob`, `WorldHeader` and the shared
frame. Suite **559 → 614**. No SQLite, no file, no pipeline — bytes in, bytes out, which is the
whole point of doing this part first.

### The defect the truncation test found, and it was real

`ATruncatedColumnIsRefused` cuts a blob to every possible length and expects a refusal at each.
Two of them passed the blob through: **exactly the last five bytes removed, in both the column and
the world header.**

The mechanism is worth keeping. Raw deflate carries **no integrity check at all**. Cut a stream
after the last byte the decompressor actually needed — the four-byte Adler and the byte carrying
the end-of-stream marker — and the inflater has already emitted every byte it was going to, so it
reports a clean end of stream and *every* check passes. Removing **fewer** bytes is caught, because
the trailer is then present and wrong. The failure was non-monotonic in the truncation length,
which is why a spot check would have missed it and the exhaustive loop did not.

The fix is two things, and they are complementary rather than alternatives:

- **The frame now declares the stored length as well as the uncompressed one**, so truncation is
  caught by arithmetic before decompression starts. The class comment already claimed truncation
  was *"caught by arithmetic rather than by a decompressor error that could mean anything"* — it
  was only true on the uncompressed branch.
- **zlib rather than raw deflate**, for six bytes, so an Adler-32 covers bit-rot in the middle of
  an intact blob, which no length can catch.

**And the battery's own recipe for the second one was mis-designed**, which is worth recording
because it read as a pass. Swapping only the *reader* to raw deflate fails 17 tests — but that
proves the two halves agree, not that the checksum does anything, because `DeflateStream` cannot
read a zlib header at all. The honest recipe swaps **both** sides, leaving the format
self-consistent and removing only the integrity check. Re-run that way it fails 5, including
`CorruptionInsideAnIntactBlobIsRefused` — a test added specifically because, with the stored-length
check in place, nothing else would have failed and the six bytes would have been decorative.

### `Compact()` is retired from the save path, and M1's comment is corrected

`VoxelChunk.Compact` has carried *"this runs on serialisation"* since M1 **with no caller at all**.
M6-PLAN flagged it as a prediction rather than a decision and said to measure it. Measured over
2,048 real chunks in Release, `dotnet run --project MinerCraft.Bench -c Release -- --blob`:

```
arm                     write µs   read µs     raw B  stored B   ratio  uniform  B/varied
generated, compact         146.2      13.7      1980        90    21.9     1404       229
generated, verbatim         14.7      11.8      1980        90    21.9     1404       229
carved, compact            269.1      21.3      3170       124    25.6      827       189
carved, verbatim            19.9      21.3      3170       124    25.6      827       189
stripped, compact          111.2       6.7      1323        60    22.0     1689       216
stripped, verbatim          11.5      11.4      2014        65    31.0     1404       148
```

- **On a carved chunk — the typical save — Compact costs 13× the entire write and saves nothing.**
- **On the case it exists for, 10× the write for 7.5%** of a blob already compressed 22×. Roughly
  100 µs of CPU for five bytes.

So `ChunkBlob.Write` defaults to `compact: false`, the flag stays as the bench's A/B arm, and
`Compact`'s doc comment now says what was measured instead of what was predicted. **Revisit
trigger** recorded on it: a chunk edited across enough sessions to accumulate dead palette entries
and widen its packing — the palette never shrinks on its own and 256 entries converts it to 64 KB
of direct storage. Unreachable with five shipped materials; a heavily modded world is where it
would first bite.

**The first measurement of this was wrong, and the fixture was the trap.** The original edit pass
carved spheres of air, and carving air into a chunk that already holds air **leaves the palette
exactly as it was** — so there were no dead entries to drop and the bench reported "saves 0" from a
fixture that could not have saved anything. A dead entry appears only when the last voxel of some
material leaves the chunk, which is what the `stripped` arm now does to 644 of the 2,048. Same
family as M5c-1's shaft test, which dug a one-voxel bore that was never a sky source: **the
scenario made both arms agree, and the numbers themselves could not show it.**

### Two numbers M6b should read against its plan

- **A save file is much smaller than the plan estimated: 189 B per varied chunk**, so 185 KiB per
  1,000 stored chunks and **18 MiB per 100,000**. M6-PLAN guessed ~1 KB a chunk, so it was 5×
  pessimistic — and it was pessimistic about the half it costed, which is the usual direction.
- **Deserialisation is 21 µs a chunk, not the 0.1–0.3 ms the plan's crossover table assumed.**
  That table is M6's headline worry — that a heavily-dug world could be slower to enter than a
  generated one — and it is now half-measured. The remaining half is SQLite's per-query overhead,
  which is M6b's to take. If that lands near 30 µs the crossover moves past the whole 1,521-chunk
  fill and the worry dissolves; the plan should not be treated as settled either way until the
  other half exists.

### The world header carries thirteen fields, and a reflection test guards them

`WorldGenSettings` is not a seed — it is thirteen properties, and an unedited chunk is not stored
but **regenerated from all of them**. Change the default `CaveLatticeStep` in a later build and the
unstored terrain around a saved base comes back a different shape with the saved chunks hanging in
it. All thirteen are written explicitly.

**Explicit binary rather than JSON, and the failure mode is the reason.** A JSON reader given a
document missing a field fills in the default and carries on, which is precisely the silent
world-reshaping above. A length-checked binary layout refuses instead.

`TheSettingsLayoutCoversEveryPropertyOnTheRecord` counts the record's properties by reflection
against the constant the serialiser was written to. Adding a fourteenth field and forgetting to
write it would otherwise leave the round trip **green** — record equality would be comparing a
default against a default — while every reload silently reset it.

### Non-negotiable 2 is tested through bytes for the first time in five milestones

`Registry.ExportIdTable` and `SeedIdTable` were built at M1, documented as *"the ID table to write
into the world file"*, and had never had a caller. `AWorldSurvivesAModBeingRemovedAndReinstalled`
now runs the whole loop through serialised bytes: save with a mod installed, remove it, reopen from
the bytes alone — placeholders retaining their identifiers — reinstall, reopen, exactly as it was.

Non-negotiable 3 gets a guard rather than an assumption: **entry 0 must be `minercraft:air`**,
refused at read *and* at write. Two things depend on air being 0 — a zero-initialised array being a
valid empty chunk, and the uniform fast path testing emptiness against a constant — so a world file
where it is false does not make the world wrong, it makes a constant the whole engine assumes into
a lie. Refused at write as well, because a table that wrong reaching disk is a bug upstream and
catching it there names the session that produced it rather than the one that later failed to open
the world.

M3c's recorded item is closed at the same time: *"`MaterialTable` indexes raw, so a material ID
past its `Count` throws `IndexOutOfRangeException`… the fix is validating chunk contents at load:
M6."* `ChunkBlob.Read` takes the table and validates every material in all three storage kinds —
**required, not a separate call the caller might skip**, which is the M5c-2 shape of hole.

### The oracle, and the two things that break its symmetry

Round-trip identity, **voxel for voxel and not representation for representation** — a compacted
chunk may legally come back uniform, and asserting on `Kind` would fail on working code.

Its limit is stated rather than trusted: **a round trip runs the same writer and the same reader,
so a symmetric bug passes.** A field written to the wrong offset and read from the wrong offset
agrees with itself perfectly. So two tests share no code with the half they check — a written blob
decoded by hand with BCL only, and a hand-built blob handed to the reader. The first is what pins
the one mistake the type system cannot prevent: **writing the palette as per-chunk indices rather
than world material IDs**, which round-trips flawlessly inside one session and decodes to the wrong
materials the moment a mod moves.

One thing that fell out of writing it: **`Compact` reorders the palette into scan order**, so air
moves from entry 0 to last. That is the observable difference between the two arms and is now
pinned, because it is what makes "the oracle is the voxels" a claim with teeth rather than a
slogan.

### The battery: 19 recipes, and the two that were not straightforward catches

- **18 caught outright**, most by exactly the test written for them.
- **One green: the packing-width check in `VoxelChunk.FromPalettised`.** `ChunkBlob.ReadPalettised`
  validates the width before calling, so the factory's own check is unreachable through the blob
  path — the M4a zeroable-contact-epsilon category, belt and braces rather than a hole. **Both are
  kept**, because a validating factory for untrusted bytes must not depend on its caller having
  checked, and it is not decorative: `SetWidth(3)` computes `TrailingZeroCount(3) = 0` and a value
  mask of 7, which is a silently wrong packing rather than a crash. Four tests now reach
  `FromPalettised` directly, which turns an unreachable duplicate into a tested one — and re-running
  the recipe now fails.
- **One mis-designed**, described above.

### Also

- **`SkyColumn` gains `HasEdits`, set inside `OnVoxelChanged` rather than by its caller** — a caller
  that forgets loses a shaft's lighting with nothing failing to say so, the same argument that puts
  `HasEdits` inside `SetVoxel`. Its test is *"the height moved"*, not *"a voxel changed"*, which is
  the tight one: a column dug out well below its top is still identical to what `Seed` produces and
  is correctly never written. Digging is most of this game, so that is most of the columns.
- **A restored column comes back already marked edited.** It was only written because its heights
  moved, so coming back pristine would mean being dropped unwritten on the next unload — losing the
  shaft on the *second* visit rather than the first, which is a much worse bug to be handed.
- **Blobs carry their own position and it is checked on read**, so a blob filed under the wrong key
  is a loud refusal instead of terrain appearing somewhere it does not belong. Twelve bytes, and it
  makes a blob self-describing under `sqlite3` on the command line — which is half of why DESIGN
  calls SQLite the pragmatic choice.
- **`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` flaked once more** during
  this work and passed on every rerun. Third recorded sighting. It is load-sensitive by
  construction — one worker, a wall-clock `Patience` — and during a mutation battery it is actively
  harmful, because "1 failed" reads as "the recipe was caught". Still worth pinning down before the
  next one.

## 2026-08-31 — M6 planned: the milestone where unloading becomes a write

[docs/M6-PLAN.md](docs/M6-PLAN.md), split M6a–M6d. No code. **559 tests, unchanged.**

### The structural point, and it is not "saving is hard"

Every chunk in this world has been recoverable from nothing. Worldgen is a pure function of seed
and position, light is derived in microseconds, a mesh is thrown away as a matter of routine. That
is why `ChunkStreamer.Forget` can delete a chunk record inside a tight sweep over a thousand-chunk
shell and nothing anywhere has to think about it.

**M6 is the first milestone that creates state whose only copy is on disk, and the consequence is
that `Forget` becomes a write** — on the sim thread, inside `ChunkBox.ForEachDifference`, which at
r16 hands it a 37×37 shell in a single frame. That inverts the failure model the project has been
built under. A meshing bug shows on screen; a physics bug is felt within a second; a persistence
bug destroys work that cannot be recreated, surfaces a session later, and by then the evidence is
gone. **This is the first milestone where the tests are the only thing between a defect and
permanent data loss**, and the split is organised around that rather than around the feature list.

The second change is smaller and sharper: **a chunk gains a third possible source of voxels.** It
was memory falling back to the seed; it becomes memory, then disk, then the seed — and the only
failure that matters is consulting them in the wrong order, because regenerating a chunk that had
a blob is a silent, total loss of everything in it.

### Three decisions settled before any code

**SQLite goes in a new `MinerCraft.Persistence` assembly, not in Core.** Core has **no NuGet
dependency at all** after five milestones. That is not an accident of scope — it is what makes
non-negotiable 1's multiplayer seam more than a slogan, because a dedicated server is `dotnet run`
against Core and nothing else. `Microsoft.Data.Sqlite` would be its first and carries native
binaries that have to survive a Godot export with `EnableDynamicLoading`. So `IChunkStore` lives in
Core with an in-memory implementation, which is **M1's `IContentSource` / `DirectoryContentSource`
/ `InMemoryContentSource` shape exactly**, built then for the same reason and in the same words.
`ChunkPipeline` takes an `IChunkStore?` and null means no persistence, so **all 559 existing tests
keep passing unedited** — a test suite edited in the same pass as the feature it guards has stopped
guarding it. No ADR: DESIGN already locks SQLite over region files, so this is where it lands, not
whether. Had the recommendation been to put the package *in* Core, that would have earned one.

**Saving is a job on the serial lane, and the argument that put light on the sim thread does not
transfer.** M2a built that lane for exactly this and said so; the flag has been `false` at every
submission site since. Light went to the sim thread because a BFS whose frontier crosses chunks has
no per-chunk window that is correct. A chunk blob is a pure function of one chunk, which is
`MeshChunkJob`'s shape precisely, and `fsync` on the sim thread is the textbook frame hitch. **The
argument was made fresh rather than inherited, and it came out the other way, which is the point of
making it fresh.**

**The ID table lives in the same database as the chunks, in the same transaction.** The obvious
layout — a JSON world file beside a SQLite chunk DB — is the one arrangement that can
desynchronise: a crash between the two leaves blobs full of numeric IDs whose meaning is a version
behind. Atomicity is most of what DESIGN is paying SQLite for. M6 is also where `ExportIdTable` and
`SeedIdTable`, built at M1 and **never called by anything**, get their first caller, and where
**non-negotiable 2 gets its first real test in five milestones**: save with a mod installed, remove
it, reopen, and the blocks must be placeholders retaining their identifiers rather than
reinterpreted as whatever now holds those numbers.

### Two exceptions to pipeline-wide rules, and both lose data if missed

Every race in M6b already has a documented answer in `ChunkPipeline`, written for meshes. That is
the good news and the trap together: each one has to be transferred across deliberately, and where
a mesh race leaves stale geometry on screen, the same race on the save path loses a tunnel.

- **`OnChunkUnviewed` must not cancel a save, and `Dispose` must not drop one.** Every other job in
  this engine is cancelled the instant nobody wants its chunk, and that is right for work whose only
  product is rebuildable. A cancelled save is a deleted tunnel. `JobResult`'s contract says *"a job
  still queued at shutdown reports Dropped"*, so **quitting the game with saves queued loses them.**
  This is the single most likely way to ship a data-loss bug in M6 and it is one line of `Dispose`.
- **A save job must set `VoxelsSharedWithJob`**, as `Neighbourhood()` does for all 27 of a mesh
  job's — not because a chunk being unloaded is normally written to, but because it can be
  **revived and then mined while the serialiser is mid-copy**, and M3a records that this is not a
  stale voxel but `VoxelChunk.Set` swapping the backing arrays under a reader.

**And `DirtySinceSave` is the `NeedsRemesh` problem a third time.** `HasEdits` keeps meaning
"differs from the seed" and is never cleared; the new flag means "differs from what is on disk",
clears **at submission** because `SetVoxel` will set it again if an edit lands during the job, and
must be **restored by `StepBack`** if the job drops or faults. Conflating them loses exactly the
last edits before an unload — the most recent work, and only sometimes.

### The half the plan is most likely to have got wrong, named for that reason

Every milestone here has costed one half and been silent about the other. M5-PLAN costed
propagation well (585k visits measured against 779k predicted) and said nothing at all about the
derive, which turned out to be the binding constraint. The candidate for M6 is **the read side**,
because "persistence" reads as "saving" and that is where attention goes.

Generation fills r4h/2v in **~89 ms of wall clock** — 1,521 chunks × 0.47 ms measured, across eight
workers. Loads replace generates one for one, but the serial lane runs them **one at a time**:

| per-load cost | loads before the serial lane exceeds the generate path it replaced |
|---|---|
| 0.1 ms | ~890 of 1,521 |
| 0.3 ms | ~300 of 1,521 |

**So a world the player has dug into heavily can be slower to enter than one generated from
scratch**, and the crossover is well inside what a real base would produce. Batched loads — one
job, one query, many chunks, since SQLite's per-statement overhead dominates a 16 KB blob read —
are the named lever and are deliberately not built. The other lever, parallel reads under WAL, is
the *worse* one: it contends for the eight workers generation already saturates and reopens a
decision M2a made with its reasons written down.

The save side, by contrast, is arithmetic that says stop worrying: a 4-bit palettised chunk is
16,384 B plus palette, deflate on voxel data is an estimated 10–50×, so ~1 KB a chunk and a few
hundred KB a session. **Doing that arithmetic before building anything is what moved the nerves to
the read side.**

### Two things found by reading the code that the handover did not have

**`VoxelChunk.Compact()` has existed since M1 with the comment "this runs on serialisation" and has
never had a caller.** That sentence is a **prediction, not a decision**, and M6 is where it either
becomes true or gets retired. It costs a full 32,768-voxel scan through `GetAt` plus a repack
(~0.13 ms a chunk, estimated) and buys a 2× blob saving in the realistic worst case — which deflate
mostly eats anyway. Measure it at M6a and either keep it or correct the comment; same family as
M4a's zeroable contact epsilon, a claim that was never wrong, never checked, and one line from
going stale.

**The column restore cannot go where the column is created.** `RetainColumn` runs inside `View`
inside `ForEachDifference`. A one-chunk horizontal step at r16 creates ~37 new columns; at an
estimated 20 µs per 2 KB read that is ~0.74 ms of I/O in a single frame, unamortised, against a
main-thread streaming budget M2d measured plateauing near 3% of a core. That is a spike, not a
cost. Recommended instead: seed analytically as now, queue a restore, and call
`QueueColumnLightBuild` when it lands — machinery M5c-1 built for exactly *"this column's sky
heights moved"*. The price is a one-or-two-frame window where a reloaded shaft is lit wrong.

### One correction to the handover, and it matters for what gets checked

The handover said *"M5c-1's recorded revisit trigger fires in M6, not later"* — the trigger being
*"when `ApplyFeatures` starts placing anything it must route through the relight path the way
`SetVoxel` does."* **It does not fire.** `ApplyFeatures` still places nothing, and brush placement
is a different route entirely.

The *risk* is the same one and is worth stating in full, which is what the plan does: a 1 m cube
brush is 4×4×4 = 64 voxels, and writing them straight into `chunk.Voxels` is faster, obvious, and
skips **four** things at once, all silently — the sky-column update, the light rebuild queue, the
eight-chunk border dirty rule, and `HasEdits`, which at M6 means the placement is never saved. So
`Place.*` calls `IVoxelEditor.SetVoxel` per voxel exactly as `Carve` does. But the distinction is
worth keeping: **a warning about which code path runs is not a warning about which property is
checked**, which is the lesson M5c-1 recorded about its own shaft test.

`SetVoxel`'s "material is already there" early-out finally earns its keep, exactly as its own
comment predicted: *"M6's placement is exactly the shape of caller that rewrites what is already
there."*

### Three placement decisions, recommended rather than settled

- **Brushes are staircases, not slopes.** M4d's deliberately-not list rules out non-cube collision
  shapes as *"a voxel is one material or another" violations wearing different hats*, so a "ramp
  brush" can only mean an **arrangement of 0.25 m cubes inside a 1 m cell** — a four-step
  staircase. That is M3c's *"sub-voxel means the voxel is small, not that it is partly full"*
  restated for the placement half, and it lands well: 0.25 m is comfortably inside M4b's 0.5 m step
  height, so a placed ramp is walkable by physics that already exists, with no new collision shape
  and no new anything. **A ramp's orientation is the first brush parameter that is not a scalar,
  and the first thing in this game with a rotation at all.**
- **Snap brushes to the world 1 m lattice.** Not because the cube wants it but because **the slab
  and the ramp only make sense on a lattice** — a ramp that is not grid-aligned cannot be tiled
  into stairs. The lattice is what makes the brush *set* coherent, and DESIGN's *"fine detail comes
  from carving, not from placing"* is the argument. Goes in Core on M3d's grounds, as the
  counterpart to `Carve.SwingCentre`. Revisit trigger: M6d plays it and it reads as unresponsive.
- **Material inside the player: refuse the overlapping voxels and place the rest.** Least
  surprising, and pillar-jumping still works because mid-jump the cell below is not overlapping.
  **It does not retire `PlayerSpawn.TryUnstick`** — M9's collapse puts material inside the player by
  accident and has no alternative — it stops placement being a second, *deliberate* caller of a rule
  whose only honest home is "something went wrong". `PlaceResult` takes `CarveResult`'s four-bucket
  `IsAccounted` shape for the reason that struct records: a `continue` added later and counted
  nowhere.

### Inventory, with its cost stated

**Unbounded at M6.** Bounding it now — no containers, no conveyors, no crafting — means the player
fills up and stops, a constraint with nothing on the other side of it. **The honest cost is that M6
then cannot answer whether volume is a real constraint**, so DESIGN's *"volumetric inventory makes
big excavations meaningfully productive"* stays argued rather than played. Revisit trigger: M10's
containers or M11's crafting, whichever gives capacity somewhere to go.

The arithmetic is pleasant enough to record: a 0.25 m voxel is **15.625 litres**, and a 1 m brush
cube is 64 voxels = **1,000 litres = 1 m³**. That round number is most of why the unit is worth
having. `CarveResult.Removed` is already documented as the number to convert.

### Order, and why persistence goes first

**M6a → M6b → M6c → M6d**, with persistence before placement despite placement being the more
enjoyable half and independent of it. The reason is the milestone's own headline: **play currently
loses its results, so every hour of play before persistence lands is an hour thrown away —
including the play that would judge placement.** Placement built second is placement built on a
world that keeps it, and M6c's "go and build a room" test is only meaningful once the room survives
being flown away from.

M6a before M6b for the reason M5b came before M5c: a pure function with a sharp oracle and no
threads, fuzzable in milliseconds, so a bug found in M6b is unambiguously an integration bug. It is
also the only part of M6 whose decisions cannot be cheaply undone — a format cannot be changed once
anybody has a saved world, and the person who will have one first is Matthew, mid-milestone.

### The oracle, and its limit stated rather than trusted

Round-trip identity, **voxel for voxel and not representation for representation** — a compacted
chunk may legally come back uniform, and asserting on `Kind` would fail on working code, which is
M3b's shape of an oracle wrong in the subject's favour.

**Its limit: a round trip runs the same writer and the same reader, so a symmetric bug passes.** A
field written to the wrong offset and read from the wrong offset agrees with itself perfectly. The
complement is a hand-constructed blob asserted byte for byte, one per storage kind — small, and the
only thing that pins the format rather than the pair. The break-recipe battery includes **writing
the palette as per-chunk indices rather than world IDs**, which the type system cannot prevent and
which round-trips flawlessly within one session.

### The one thing the plan deliberately did not cost, and says so

**How long a human tolerates between pressing play and standing in their own base.** Every number
above is throughput. M5c-1 found that the metric that mattered was not throughput but *time to
first draw*, and it found it by sweeping a budget against when the world could first be seen rather
than against how fast it worked. The equivalent sweep here has no obvious knob to turn, which is
exactly why it is the thing to watch for.

### Addendum: the dependency was probed rather than left as a flag

The session handed over "M6b will need a network restore" as an open risk, which was both badly
worded and easy to close, so it was closed. A throwaway console project outside the repo:

```
Microsoft.Data.Sqlite 10.0.11 restores against net8.0 under the 10.0.302 SDK
sqlite version : 3.53.3          — the native e_sqlite3 loads
blob round trip: 16384 bytes, identical = True
journal mode   : wal
db on disk     : 4096 bytes
```

Three things worth keeping:

- **M6b needs no network.** The package and its dependency chain are in the local NuGet cache now.
- **`SQLitePCLRaw.bundle_e_sqlite3` does come along**, as the plan predicted. So the native-binary
  question is real — but it is a **Godot export** question, not a restore question, and it is still
  unanswered. That is the part to check, and checking it needs an export rather than a probe. Also
  worth pinning the version explicitly rather than taking the 10.x default against a net8.0 target.
- **In WAL mode the main `.db` file stayed at 4,096 bytes** after a 16 KB blob was committed,
  because the data was in the `-wal` sidecar until a checkpoint. Harmless and completely standard,
  and exactly the observation that otherwise costs an hour of "my saves are not being written".

**The wording of the original flag is the lesson, not the result.** "Will need a network
`dotnet restore`" reads as an action to take, and `dotnet restore` cannot take a package name — it
restores what the `.csproj` files already list, and nothing lists SQLite because
`MinerCraft.Persistence` does not exist yet. The command is `dotnet add package`, against a project
M6b creates. A flag that names a command the reader cannot yet run is worse than no flag.

## 2026-08-31 — M5 is done: "happy with the current state"

Lighting is finished, end to end and judged by a person. **559 tests.** M5a vertex AO, M5b the
light grid and propagation, M5c the pipeline and the shader, M5d-1 the measurements and M5d-2 the
play.

### The last open question closes by default, and the distinction matters

**Light cell size stays 1 m.** The recorded revisit trigger was *"M5d plays a `hand_pick` tunnel
and the light reads blobby across a bore narrower than one cell"*. That specific play — a 0.75 m
bore, isolated, walked down — **was not done**: the play used the entangled miner, which digs
chambers, and a chamber is many cells wide, which is the case a 1 m grid is good at.

What did happen is a lot of play across lit chambers, dark chambers and torchlit ones, ending in
"happy with the current state", with light quality never raised. **So 1 m stands because nobody
reopened it, not because the narrow case was probed and passed.** That is silence rather than
evidence, and it is the same shape M4d recorded for step-up guard 3's diagonal wall-hug — worth
the distinction, because "tested and fine" and "never provoked" decay differently.

**The trigger therefore stays live rather than being retired**, unlike M5's fragment-shader
fallback, which was retired properly by measurement in both halves. If a `hand_pick` bore ever
reads blobby, 0.5 m cells cost 4 KB per chunk and 8× the propagation — **and since M5d-1 the
propagation is known to be the cheap half**, so the real bill is the derive, which is already the
thing under pressure.

### What M5 leaves behind

Six things are recorded and unbuilt, none of them blocking:

- **Two levers on the derive**, measured not named: `PaddedChunk.CopyFrom` at 59% of it (~1.7×),
  and re-derives at 2.4–6.0 per chunk (~2.4×, the bigger prize). Together they would move the
  12–20 m/s draw cliff past the flycam's 30 m/s sprint.
- **A local lighting flicker on a chunk boundary**, seen and deliberately not chased —
  *"rotating back and forth on a specific angle, but it was negligible"*. **The reason not to
  chase it is recorded, and it is not "it is small":** the fix would mean complicating the
  lighting maths, and this milestone is already balancing a 1 m grid against 0.25 m voxels, two
  channels in a merge key, a sim-thread budget, and a derive cost that turned out to be the
  binding constraint. **A negligible artefact does not earn a new term in the most delicately
  balanced system in the project.**

  One hypothesis is eliminated in the record, and it is the obvious one: it **cannot** be the
  viewer chunk oscillating at the boundary, because `FlyCam`'s mouse-look assigns `Rotation` and
  never `Position`, so rotating cannot move the viewer between chunks and nothing in the
  streaming or relight path is reachable by rotation alone. That leaves render-side causes —
  most likely coincident faces across the boundary resolving differently by angle, which would
  be *geometry* wearing a lighting costume. **Revisit trigger: when it stops being negligible**,
  or when anyone reports a dark patch that does not go away — which is the classic voxel bug
  M5-PLAN opens the milestone by naming, so this sighting is filed in the right family even
  though it is harmless today. The repro is the expensive half of any future investigation and
  it already exists.
- **Whether the big evenly-lit cavern's ceiling was perforated** or skylight is leaking. The
  enclosed spaces in the same session went properly black, which argues against a leak.
- **Whether the 12–20 m/s cliff is felt at the flycam's normal 6 m/s.**
- **15 m of light is 60 voxels** — an observation, with a one-number content lever if it ever
  stops looking good.
- **`screenshot.gd`'s 8 s settle** predates light and comes back empty at r10.

### The milestone's own verdict on itself

M5 set out to answer whether a 1 m light grid is enough resolution for a 0.25 m world and whether
AO makes voxels read as sculpted rather than as cubes. **Both are yes**, one of them provisional
in the way described above. And the milestone dissolved its **sixth** named fallback by
measurement — the fragment-shader path, killed in both halves, the first one ever dissolved in
the wrong direction (light in the merge key makes quads *cheaper*).

The one thing it got wrong was its own cost model: it costed the propagation, said nothing about
the derive, and the derive turned out to be the whole of the expense. **Ask what the plan put no
number against at all** is the lesson, and it is now the second milestone to teach it.

---

## 2026-08-31 — Hand mining one block at a time: deferred, and the entangled miner is why

Raised again after the entangled miner landed: *"For hand mining I was going to make you do it
one block at a time... then we would need to discuss tools and tiers etc, but having said this I
suppose you can draw conclusions."*

**Conclusion drawn: don't change the default now.** The sphere stays the bare click. Not because
the idea is wrong — it is a better *game* — but because it is the second half of a change whose
first half does not exist yet, and shipping the second half alone buys the tedium without the
progression.

### What actually changed since the decision, and it is not nothing

The decision was made 2026-08-30 on the grounds that a single-voxel default is **~17× slower to
excavate with the starting pick**, in a game about a mining operation, against DESIGN's own
argument that volumetric inventory exists to make big excavations productive.

**The entangled miner moves that argument's premise.** "The starting tool is slow" is *tedium*
when it is the only tool and *progression* when there is a ladder — and yesterday there was no
top of the ladder, so slow meant slow forever. Now there is a top.

What there is not is a **middle**:

```
hand_pick     radius 0.40 m   hardness 1.0   cooldown 0.30
steel_pick    radius 0.50 m   hardness 3.5   cooldown 0.20
   ...        nothing
entangled     the whole world, 8/s, 60 voxels of reach
```

Two rungs 0.1 m apart, then a cliff. **So the thing the entangled miner exposed is that the
missing piece is the ladder, not the granularity** — the granularity question is downstream of
it, and answering it first fixes the early game's pacing before the thing that would relieve it
exists.

**Crafting is M11.** That is where a tier ladder becomes real content rather than two JSON files,
and it is the earliest point at which "the starting pick is slow" is a design statement instead
of an apology.

### A number the original decision did not have

17× was measured per **voxel removed**. Per **metre of walkable tunnel** it is worse, because a
single-voxel tool has to clear a cross-section rather than a bore:

- The player is 0.5 m wide and 1.8 m tall, so a walkable tunnel is at least 2 voxels across and
  ~8 tall — **16 chips per 0.25 m of advance, 64 per metre.** At `hand_pick`'s 0.30 s cooldown
  that is **~19 s per metre**, so a 10 m tunnel is over three minutes of clicking.
- The sphere clears the same metre in roughly one or two swings.

**Estimate, not a measurement** — the cross-section is arithmetic off M4a's box and M4b's step
height, not something anyone has dug. It is the right order, and it says the honest cost is
nearer **40× than 17× for the thing a player actually does**, which is advance a tunnel rather
than remove a voxel.

### Why deferring is free

`Carve.Chip` and `Carve.Swing` both exist and share one hardness gate; which one a bare click
calls is **one line in `MiningController`**, and `radiusMetres` is content. So the decision costs
nothing to make later and quite a lot to make now. That is the whole shape of it: **cheap late,
expensive early.**

**Revisit trigger, in Matthew's words: "then we would need to discuss tools and tiers etc."**
When that discussion happens — M11 at the latest, and it can happen sooner since tools are
already JSON — the single-voxel default should be on its agenda rather than settled separately.

---

## 2026-08-31 — M5d-2: torchlight, and 15 m of light is *sixty voxels*

**"OOhhhh ahhhhhh Torchlight! Yellow coloured too."** One lamp, underground, and the chamber is
warm. That is **five of M5d-2's six questions answered**, and only "is 1 m too coarse down a
`hand_pick` bore" remains — which needs a narrow tunnel and cannot be asked of a chamber.

**It is the only test in the whole milestone that could have proved this.** M5c-2 recorded that
every other check would pass identically if both light channels were packed into one byte: the
merge key, the trilinear sample, the quad counts, the shader — all of them. Only a **warm pool
in a dark room** shows that `COLOR.g` carries block light, is tinted separately from `COLOR.r`,
and survives the whole path from `MaterialDefinition.LightEmission` to a fragment.

**And then a second shot went one better: one lamp in a skylit cavern, both channels in the same
frame.** The cavern is cool grey — `COLOR.r` through `sky_tint` — with a golden pool around the
lamp — `COLOR.g` through `block_tint` — and a clean spatial boundary between them. The first
shot proved block light exists and is warm. **This one proves the two channels do not contaminate
each other**: the additive composite `shade.x * sky_tint * sky_brightness + shade.y * block_tint`
blends two differently-coloured fields into something that reads as one lit space rather than as
a muddy average. That is the whole return on the vertex bytes M5a spent keeping them apart, and
it is the picture to point at when somebody asks why they were not packed into one. It is also
what DESIGN's M7 wet-surface term depends on — *"skylight × recent rainfall"* needs skylight on
its own — so this shot is the receipt for the two-channel decision that M5a paid for in vertex
bytes.

### The observation worth keeping: the reach is right in metres and enormous in voxels

`lightEmission: 15`, attenuation 1 per 1 m cell, so a lamp reaches **15 m — which is 60 voxels**.
In a game whose corridors are three to five voxels wide, one lamp lights a whole working.

**This is the same trap the cell size already carries, one level along.** M5-PLAN records it for
the grid: *"DESIGN reasons in metres — light is low-frequency, nobody sees the difference — and
the number that decides whether it looks right is in voxels."* The emission range has the
identical shape and it was not written down: TODO already calls 0..15 **"a game-balance number
wearing a storage costume"** and notes the brightest lamp reaches 15 m *for a storage reason
rather than a design one* — what it does not say is that 15 m at a 0.25 m voxel is four times the
relative reach a 1 m-block game gets from the same number.

Not filed as a defect. It is exactly what the numbers say should happen, it looks good, and
nobody has complained. Recorded because **the lever is one number in one JSON file with no
rebuild** — `lightEmission` in `mods/minercraft/materials/lamp.json`, the same shape of content
lever `radiusMetres` is for the bore — and because a future "why does one lamp light everything"
should find this entry rather than rediscover it. Halving it to 8 gives a 32-voxel pool.

---

## 2026-08-31 — M5d-2, played: a mine reads as a mine, and AO earns its quads

Matthew dug a chamber with the entangled miner and looked at it. **"Looks good to me."** Four of
M5d-2's six questions are answered by eye, which is the only way they could be answered:

- **Does a mine read as a mine? Yes.** One chamber runs from lit rock to *actual black* across
  its width, and the falloff reads as depth rather than as a rendering artefact. A second shot
  is near-total darkness with a handful of faces catching a little light. **Darkness is real**,
  which is the thing M5 exists to supply and M13 turns into a hazard — and it is the first time
  in this project that a screenshot of nothing is a pass.
- **Does AO earn its quads? Yes.** Visible on every block edge and corner, and it is what makes
  the excavation read as sculpted rather than as a pile of cubes. DESIGN calls it *"the single
  biggest visual payoff"*; that was a claim, and it is now a person's verdict. It costs **+87%
  quads and +6.3 MiB on its own** (M5d-1), and the shipped AO+light arm costs less than AO
  alone, so the price was already known to be worth paying arithmetically. Now it is worth
  paying visually.
- **Are the face-tint constants right now the sun is gone? Yes, not raised as a problem.** Top,
  side and bottom faces are distinguishable in the dark chamber, which is the whole job they
  took over from the directional light. No retune asked for. They live in `Voxel.gdshader` and
  deliberately not in the merge key, so retuning them costs no remesh whenever it happens.
- **Does rock beside a lit tunnel glow? Not raised.** The transition to black reads clean. This
  was the known cost of a per-cell value, half-mitigated by the half-cell normal offset, and it
  appears to have been mitigated enough.

**The fog fix is load-bearing for all of the above.** Every one of these shots would have been a
legible grey-blue landscape before it — the "black" would have been the fog's colour, and the
gradient that reads as depth would have been distance, not light.

### Two still open, and both need a *narrow* bore rather than a chamber

The entangled miner digs chambers, which is what it was built for and is why the questions above
got answered. It is the wrong instrument for the two that remain, because **both are about
tunnels narrower than one light cell**:

- **Is 1 m too coarse down a `hand_pick` tunnel?** The bore is 0.75 m and the cell is 1 m, so the
  cell is wider than the corridor. **This is the play that would reopen the settled 1 m**, and a
  chamber cannot ask it — a chamber is many cells wide, which is the case the grid is good at.
- **Can you mine by torchlight?** No lamp in any shot. `L` places one.

### One thing I could not settle from a picture

The third shot is a large cavern lit fairly evenly, with a single small sky opening visible at
the top — grass and a sliver of sky. Skylight falls free at full strength straight *down* but
attenuates 1 per metre *horizontally*, so one small hole should light a pool about 15 m across
and no more. Either that ceiling is thin and perforated out of frame, in which case the cavern is
lit legitimately, **or skylight is spreading further than the model allows**, which would be a
leak in the cell-open-to-sky test or in conductance.

**The other two shots are the evidence that it is probably not a leak**: an enclosed space in the
same session goes properly black, so darkness is not being flooded generally. Asked rather than
assumed. Recorded here because a "looks good" that quietly included a leak would be the most
expensive kind of pass.

---

## 2026-08-31 — The entangled miner goes to eight a second, and the raise inverts a caveat

One constant, `MiningController.EntangledCooldownSeconds` 0.25 → 0.125. Recorded separately
from the entry below because it **invalidates a claim that entry makes**, and a superseded claim
left standing is worse than no claim.

**At four a second the mode was not strictly an upgrade**: `steel_pick` alone cools down in
0.20 s, so entangled was marginally *slower* per swing and the only upgrade was the range. **At
eight it is faster than every shipped tier**, so it is now a strict upgrade on both axes — which
is what an endgame tool ought to be, and worth saying out loud because it means **the tier's
cooldown no longer bounds anything while entangled**. A tier balanced by its rate would be
balanced by nothing here. Nothing shipped is balanced that way today; the note is for when
something is.

Re-measured rather than assumed, same configuration as the four-a-second run:

```
                        4/s run        8/s run
swings                     69            134      almost exactly double
swing ms mean           0.139          0.072      cheaper per swing
remesh per swing          0.7            0.7      unchanged
        per second        3.4            6.3      doubled, as it should
relight per swing         0.9            0.5      halved
        total              61             72      barely moved
main thread             76.5%          75.9%      unchanged
```

**Doubling the rate cost nothing measurable**, and two of those rows are worth keeping:

- **Swings got cheaper, not dearer.** Consecutive swings land closer together, so more of each
  sphere is already air — 13 already-air voxels per swing against 11, and 20 removed against 22.
  A faster tool does *less* work per swing because it is chewing its own previous bite.
- **Relight remeshes per swing halved.** Same mechanism: consecutive swings repair light in a
  region the previous swing already repaired. The absolute total barely moved despite twice the
  swings. So M5d-1's "1.0 relight remesh per swing" is an upper bound that gets *better* under
  sustained digging, which is the direction that matters — the fragment-shader trigger it
  retired was about a rate mining can sustain.

The sim thread stayed at ~76% because it is dominated by light *derivation* from streaming, not
by mining, which is M5d-1's finding holding under a doubled edit rate.

---

## 2026-08-31 — The entangled miner, a debug bind for digging a test volume

Asked for mid-play-test and built the same day: **point at anything, hold left click, it eats
what it is pointed at however far away**, because entanglement is not distance-sensitive. Fires
at 4 swings a second. **Q** toggles it. No visual effect — the existing wireframe highlight
already follows the crosshair to arbitrary range, which turns out to be all the aiming aid it
needs.

It exists because M5d-2's questions are all about what a *large* excavation looks like — does a
mine read as a mine, is a 1 m light cell too coarse for a bore narrower than one cell, does rock
beside a lit tunnel glow — and digging that by hand at 5 m of reach is why they are still open.

### It brushes a decision locked twice, and does not reverse it

**Reach is the player's, not the tool's** — settled at M3d, restated at M4c, and argued both
times on the grounds that putting it in the tool JSON would *"guess a field into a format mods
have written against"*. A tool whose defining property is unlimited reach is exactly the case
that decision did not anticipate, and for this one tool the reversal is arguably right: the
reach really is a property of the mechanism rather than of the arm holding it.

**So it is deliberately not a tool tier.** No `reachMetres` in the JSON, no format change, no
new `ToolDefinition` field. It is a mode on `MiningController`, on a debug bind, in the same
spirit as the L lamp: once that field exists every mod writes against it and the locked decision
is gone for *every* tool rather than for this one. **Making the entangled miner real content is
the moment to settle that**, and it wants its own planning pass. Recorded in TODO rather than
decided here.

It **composes with the number keys** rather than replacing them: it changes how far and how
fast, never how big a bite or what the bite is hard enough to take. Both of those stay the
tier's, which keeps `Carve.Take`'s hardness gate the only gate — the property M3c pinned with
nine tests.

### What "no matter how far away" actually means, and it is not 512 m

The constant is 512 m and it is a backstop, not the limit. **What stops the ray is the edge of
the loaded region**, where an unloaded voxel returns `RayOutcome.Blocked` and the swing is
refused — M3b's `Blocked`-is-not-`Missed`, doing exactly the job it was built for. Entanglement
is not distance-sensitive but **the streamer is**, so the honest reach is "as far as the world
exists", about 144 m at r16. The constant only has to exceed that and bound the DDA if it ever
does not, the same shape as `ToolDefinition.MaxRadiusMetres` bounding the carve.

Aiming at sky costs nothing: the cast returns `Missed` or `Blocked`, the button does nothing,
and no cooldown is spent.

### Measured, because a bind nobody has run is a bind that does not work

F12 cannot be pressed headlessly, so `--entangle` is the sixth `--` seam of this shape. With
`--mine` it aims **along the flight path** rather than straight down, which is the one thing
that needed the flag: pointing along the path was rejected for `--mine` in the first place
because at 5 m of reach it *"would spend most of the run pointing at sky"*, and with the reach
bounded by the world instead it finally hits the hillside ahead.

```
mining    69 swings, 22 removed / 0 too hard / 11 already air per swing
remesh    0.7 chunks per swing, 3.4/s | relight 0.9 per swing, 61 total
entangled 69 swing(s) — NOT comparable with M3d's baseline
reach     11.1 m mean, 18.9 m furthest
clipped   0 swing(s) reached past the loaded world
swing ms  mean 0.139  p50 0.074  p95 0.274  p99 0.708  max 2.802
```

**18.9 m against the player's 5 m is the wire proven**, and it is limited by terrain rather than
by reach — the flythrough camera sits 4 m above a hilly surface, so the next hillside arrives
long before the loaded edge does. The bite is unchanged at 22 voxels, `0 too hard` says the
hardness gate still runs, and `clipped 0` says nothing was refused for range.

The longer cast costs **0.139 ms a swing against 0.083 un-entangled** — 1.7×, and still two
orders of magnitude inside a frame. Relight remeshes are **0.9 per swing against 1.0**, so
carving a hundred metres away is not a different cost from carving at arm's length. That was
the thing worth checking, since M5d-1 had just measured the near-field number.

**`MaxReachMetres` and `TotalReachMetres` exist only because nothing else could see this.** Swing
count and voxel count are byte-identical whether the cast ran to 5 m or to 90 — the M5c-2 shape
of hole, where every layer is tested and the wire between them is not. A mean of 4 m under an
"unlimited reach" label is the failure those two counters catch.

### Two small ones

- **Q rather than F12**, asked for and agreed. Q is free (WASD leaves QE alone), mnemonic, and
  under the left hand already, which matters for a toggle used mid-dig rather than once at
  startup. F12 is also the Steam overlay's screenshot key, a bad neighbour for a play bind.
- **The rate is fixed at 4/s and is not strictly an upgrade.** `steel_pick` alone cools down in
  0.20 s, so entangled is marginally *slower* per swing than it is; the upgrade is the range. A
  predictable rate is worth more in a thing whose job is excavating a comparable test volume,
  since otherwise the dig rate depends on which number key was pressed last.
- **Refused during a profile run** when toggled by hand, the same as walk mode, because `--mine`
  is pinned to the un-entangled sphere so M3d's baseline stays comparable. `--entangle` is the
  deliberate opt-in, and the report says outright that such a run is not comparable.

---

## 2026-08-31 — M5d-1: the measured half, and light turns out to bound how fast you can move

Done, and it is the measurement half only — **the played half is Matthew's and is listed at the
bottom.** Suite **558 → 559**. Nothing about the light algorithm changed; what changed is that
the light step is now timed directly, the standing-start fill is timed, `--speed=0` is askable,
and the fog no longer lights the dark.

### What M5-PLAN asked for, and what it got

| M5-PLAN asked for | Answer |
|---|---|
| Quads per chunk, AO off / on / AO+light | 263 / 493 / 404 per drawn chunk — M5c-2's figures reproduced exactly |
| Mesh memory, which follows quads | 32.8 / 39.1 / 37.9 MiB static. **It follows quads, including downwards** |
| Light propagation cost per frame, timed directly | `light ms` is a series now; Release: **0.241 ms/chunk deriving, 47 ns/cell propagating** |
| Remeshes per swing, into a lit space | **1.0 relight + 0.5 edit per swing**, against a ~16 trigger |
| Initial fill from a standing start, vs ~0.25 s | **3.4–3.8 s in Debug**, ~1.6 s projected in Release |
| Do not read `frame ms` | Fifth outing, below. It was worth the warning again |

### Both halves of M5's fragment-shader trigger are now retired

M5c-2 killed the quad half. This kills the other one. Mining continuously at r6h/3v and 8 m/s,
87 swings over a 15 s window, digging straight down from open sky — which is *repeatedly
breaking into a lit space*, five times a second, and is the strongest relight case available
headlessly:

```
remesh    0.5 chunks per swing (the edit)
          1.0 chunks per swing (the relight), 89 total
```

The recorded trigger was *"a single swing into a lit space triggers more than ~16 chunk
remeshes at a rate mining can sustain"*. Measured 1.0, so **16× of margin**, and the fallback
that M5-PLAN kept in reserve for two milestones is now retired rather than deferred. That makes
**six** named fallbacks in this project dissolved by measurement and none built.

### Mesh memory follows quads, including downwards

Four arms, standing start, r4h/2v, and all four draw the identical 165 chunks — which is the
whole reason the standing start is the right instrument rather than a flythrough:

```
                     quads   quads/chunk   vertices   static memory
  AO off, light off  43340       263        173360      32.8 MiB
  AO on,  light off  81331       493        325324      39.1 MiB
  AO off, light on   46831       284        187324      33.5 MiB
  AO on,  light on   66704       404        266816      37.9 MiB
```

AO costs **+6.3 MiB**; adding light *on top of AO* **gives back 1.2 MiB**. The delta works out
at ~174 bytes per quad, which is four vertices plus indices and is the right order. M2d's
"memory is mesh-dominated" finding survives M5 with the shipped arm cheaper than the AO-alone
arm in both quads and bytes.

### The finding this milestone exists to have found: light bounds how fast you can move

Light derivation is **one sim thread against generation's eight workers**, and a chunk that is
not lit does not mesh. So the symptom is not a frame spike — it is `drawn` going to **zero**.
A flythrough at r4h/2v, Debug, 15 s after a 3 s warmup:

```
speed   drawn   first draw   main thread   derives / tracked
 6 m/s    160      3.83 s        35.5%        4250 / 1521  = 2.8x
12 m/s    188      5.21 s        60.5%        6751 / 1521  = 4.4x
20 m/s      0      never         76.6%        9185 / 1521  = 6.0x
40 m/s      0      never         76.2%        6730 / 1521  = 4.4x
```

**The cliff is between 12 and 20 m/s, and the flycam's sprint is 6 × 5 = 30 m/s.** Holding Shift
in fly mode stops the world drawing at all, in the Debug build `start.cmd` runs. At 20 m/s and
above the light step spends its entire 4 ms budget every frame, the backlog never clears, and
the barrier is never satisfied.

For comparison, M3d's streaming-only control at r6h/3v and 8 m/s was **0.80% of one core**; the
same configuration today is **69.5%**. Light being on the sim thread is a deliberate and
well-argued M5 decision — *"a BFS whose frontier crosses chunk boundaries freely"* — so the
**kind** of cost is by design. Its **size** is not: M5-PLAN costed the propagation and said
nothing at all about the derive, and the derive is the whole of this.

### Release says how much of that is Debug, and the answer is most of it

The game project builds Debug, so every number above is a Debug number. `MinerCraft.Bench`
gained a `--light` mode — the same instrument M2d-2 used for caves, pointed at the other half of
the pipeline, loading `mods/minercraft/` through the real loader because conductance is a
function of which materials are opaque and a fixture would measure a made-up world:

```
dotnet run --project MinerCraft.Bench -c Release -- --light

derive    493 ms total, 0.241 ms/chunk
  copy    289 ms of that (59% of the derive), 0.141 ms/chunk
  uniform 281 / 2048 chunks took the closed form (14%), skipping the copy entirely
drain      25 ms total over 524855 cell visit(s), 47 ns/cell
together  0.253 ms/chunk on one thread
```

Three things fall out:

- **0.241 ms/chunk confirms M5c-1's 0.245 independently**, on a different harness with the
  shipped materials. That number is now measured twice and can be relied on.
- **`PaddedChunk.CopyFrom` is 59% of the derive**, which M5c-1 estimated at 60–75% and did not
  publish a measurement for. It is now measured, at the bottom of its own estimate.
- **Debug costs 5.9× Release on the derive** (1.41 vs 0.241 ms/chunk) and ~10× on the walk. So
  the 69.5%-of-one-core figure above is ~12% in Release, and the 12–20 m/s cliff is a Debug
  cliff. It is still the build a person plays.

### M5-PLAN's propagation estimate was good, and M5c-1 only checked half of why

M5c-1 recorded *"its propagation estimate was actually good — 585k visits measured against 779k
predicted"*. That checked the **count**. The plan's estimate had two factors and the other one
— ~20 ns per visit — had never been checked at all. It is **47 ns** in Release, so the plan's
15 ms total against a measured 25 ms: **1.7× out, which is a good estimate.** The conclusion
does not change, but it now rests on both factors rather than on one and an assumption.

**A caveat that came free, and it invalidates any single ns/cell figure:** the fall profile
reports **50 ns/cell** in *Debug* while the standing start reports **460 ns/cell** in the same
build. A falling player propagates mostly full-skylight free fall, which is the cheap path, and
9.4M cheap visits price differently from 585k general ones. **The per-cell cost is a property of
the mix, not of the code** — quote it with the scenario or not at all.

### M4d's headline claim survives, and the reason is worth stating

Re-ran the fall profile with light in the loop, r4h/2v, terminal 40 m/s, 250 m drops:

```
held      0 step(s) on unloaded world (0.00%), longest run 0
contact   0 ground-state flicker(s) over 120 resting step(s)
```

Still zero. **The player cannot outrun the streamer even though the world cannot draw** — the
collider asks for voxels and the barrier gates meshes, so light bounds what you can *see*, not
what you can stand on. The same run reports `0 drawn`, which is M4d's own recorded limitation
("a falling player falls through air") compounding with this milestone's.

### The fog was not ignoring the light grid; it was the brightest thing in a dark world

TODO carried this as *"the fog ignores the light grid — a dark world still has a fog-coloured
horizon"*, which understates it by a lot. Godot's `WorldEnvironment` fog blends the **final**
colour toward one constant, so against terrain the light grid has correctly rendered black, the
fog is 100% of what you see. Shot at `--skybrightness=0`, before and after:

- **Before:** a fully readable grey-blue landscape, contours and all, in a world with no light
  in it whatsoever.
- **After:** black to the horizon, a crisp silhouette against the sky.

The fix is `render_mode fog_disabled` on the voxel material plus our own exponential fog in the
fragment shader, fading toward `fog_colour × sky_brightness × COLOR.r` — the fragment's own
**sky** exposure, because fog is in-scattered daylight and a lamp a metre away does not scatter
across a room. Same density and same colour, so a daylit surface is pixel-for-pixel what it was;
verified with a before/after pair at the default radius.

Two things about it that are easy to undo by accident:

- **The environment fog stays enabled, and only the voxel material opts out.** Removing
  `fog_enabled` from `Main.tscn` altogether *also* un-fogs the **sky**, which exposes the
  procedural sky's dark ground hemisphere as a black band along the horizon. That is why
  `fog_disabled` is a per-material render mode. Caught by a screenshot and by nothing else.
- **The fog multiplies by `sky_brightness` as well**, so it stays on the diurnal uniform. Night
  fades to black with no reflooding, which is the same property the uniform exists for.

### Two levers on the derive, both now measured rather than named

M5c-1 named `PaddedChunk.CopyFrom` and deliberately did not build it, on the grounds that
raising the budget bought back the whole regression. The budget cannot buy this one back — at
20 m/s the work exceeds the frame however it is sliced.

1. **`PaddedChunk.CopyFrom`, 59% of the derive.** Measured, not estimated. It recomputes the
   neighbour index per voxel where 27 block copies would do, and it would speed up **meshing**
   too, since a mesh job builds one per chunk as well. Ceiling ~1.7×.
2. **Re-derives: 2.4–6.0 per chunk, against 1.0 at a standing start.** A chunk re-derives every
   time a neighbour appears where it had assumed air. M5c-1 wrote the trigger for this in
   advance — *"a ratio that climbs is the signal that `LightNeighbours`'s gain-only rule has
   stopped being enough"* — and the ratio climbs monotonically with speed. Ceiling ~2.4×, and
   larger than the copy.

Together ~4×, which would put the Debug game around 17% of one core at 8 m/s and move the cliff
past the flycam's sprint. **Neither is built. Both are M5e or M6 work and want their own
planning pass**, and the second one is the bigger prize, which is not where M5c-1's attention
was pointing.

### Instrumentation, and the one test it earned

`ChunkPipeline` gained `LastLightStepMillis`, `LightDeriveMillis` and `LightPropagateMillis`,
read off the same injectable clock the budget uses. The two halves **deliberately do not sum to
the whole**: what is left over is marking the relit chunks and releasing the waiters, which is
bookkeeping proportional to the changed set rather than work belonging to either half — folding
it into the walk would inflate exactly the number M5-PLAN already overestimated.

`TheLightStepsTwoHalvesAreContainedInItsWhole` pins that under a fake clock advancing a
millisecond per read: both halves non-negative, both inside the step. It is an invariant rather
than a duration, so it holds under a real clock too. **Watched fail** on the recipe "time the
drain from `entered` instead of `derived`", which double-counts the derive. It carries the
loop-ran companion assertion, because every assertion in it is inside a `while`.

**`--speed=0` is now askable.** A profile defaulted 40 m/s whenever speed was zero *or unset*,
so the one arm M5d's fill measurement needs — a standing start — was the one arm that could not
be requested. `_speedGiven` is the distinction.

### The headless frame-time trap, a fifth time, exactly as predicted

The fall profile reported `frame ms` **max 55.168** against a directly-timed `update ms` max of
**9.038**. M5-PLAN said in advance there was *"no reason to expect a fifth encounter to look
different, and every reason to expect one"*, and there was not. The 55 ms is not our work.

### Still open, and it is the half a person has to do

Nothing below can be answered headlessly. `--skybrightness=0 --debuglamp` produces a warm pool
about 15 m across fading to true black, which is the first time that shot has been worth
looking at, because until the fog fix the black was grey.

- Does a mine read as a mine?
- Is 1 m too coarse down a `hand_pick` tunnel whose bore is narrower than one cell — the play
  that would reopen the settled 1 m?
- Does rock beside a lit tunnel glow?
- Does AO earn its quads? DESIGN calls it *"the single biggest visual payoff"*, which is a claim.
- Are the face-tint constants right now the sun is gone?
- Can you mine by torchlight?
- And new: **does the 12–20 m/s cliff read as a bug while flying normally at 6 m/s?** The
  numbers say 35% of one core and a 3.8 s fill at that speed. Whether that is felt is a
  different question from whether it is measurable.

---

## 2026-08-30 — M5c-2: the sample, the sun goes, and light in the merge key makes quads *cheaper*

Done. Suite **542 → 556**. `PaddedLight` is gathered on the sim thread at submission; the mesher
samples it trilinearly per vertex and multiplies occlusion into both channels;
`Voxel.gdshader` renders `unshaded` with a `NORMAL`-derived face tint and a `sky_brightness`
uniform; and the `DirectionalLight3D` is out of `Main.tscn`. **Lighting is now end to end — a
lamp glows.**

### The headline number is the opposite of what the plan expected

M5-PLAN's recorded trigger for abandoning per-vertex light was *"light in the merge key costs
more than **2×** the quads of AO alone"* — against M5a's measured 492, a threshold of ~984.
Measured headlessly at r4h/2v, from a standing start:

```
                     quads per drawn chunk
  AO off, light off          262      the M1c constant-shade baseline
  AO on,  light off          492      the M5a baseline
  AO off, light on           283
  AO on,  light on           404      <- the number that decides it
```

**404 is 0.82× the AO-alone baseline. Light in the merge key does not cost quads; it saves 18%
of them**, and mesh memory follows quads.

The mechanism is the shade being `occlusion × light` rather than occlusion beside light — which
`CornerShade` argues for on rendering grounds (AO is geometric darkening, so it multiplies into
both channels and stays out of the shader). **Where the light is zero the product is zero
whatever the occlusion was**, so faces that could not merge under AO alone merge freely in the
dark — and the dark is most of a mine's surface area. The merge stays *exact*: the shades really
are equal and the faces really do render identically.

So **the fragment-shader fallback is not merely unnecessary, it is backwards**, and the trigger
that would have reopened it can be retired rather than left pending. That makes it the fifth
named fallback in this project to be dissolved by measurement, and the first to be dissolved in
the wrong direction. `InDarknessEveryOcclusionLevelCollapsesToTheSameShade` pins the mechanism,
because the quad measurement now depends on it and an ambient floor added to the shade *byte* —
rather than to the shader, where it belongs — would silently undo it.

### The plan's 10³ grid is one ring short, and the reason is load-bearing

M5-PLAN derives the padded light grid as **10³ over −1…8**: a vertex at the chunk's far corner
sits at cell coordinate 8.0, interior centres run 0.5…7.5, so it needs cells 7 and 8. That is
right for a sample taken *at* the vertex — and the sample is taken **half a cell along the face
normal**, which pushes the far face to 8.5 and reaches cell 9.

**The offset is not a refinement.** A face sits exactly on the boundary between a solid voxel and
an open one, so a vertex on it interpolates equally between the lit cell and the dark rock: a
flat, fully lit floor would render at **7.5 of 15, everywhere**, and would look like a deliberate
art choice rather than a bug. `ChunkLight` recorded the offset at M5b as the mitigation for rock
near a lit tunnel glowing; what it did not do was feed it back into the grid size.

So **11³ = 1,331 bytes**, and cell 9's interpolation weight there is exactly zero — the sample
along the face axis is `plane / 4 ± 0.5` with `plane` an integer, so at `plane == 32` the weight
is 8.0 − 8 = 0, exactly, in binary. Clamping the index instead would be correct today and
silently wrong the first time the cell size or the offset changes, for 331 bytes.

### The sun is gone, and it retired two open items at once

`Voxel.gdshader` renders `unshaded`; there is no directional light and no Lambert term. Verified
by screenshot, which is the only instrument that works — headless has no renderer, so a shader
error does not even surface there.

- **The bright and dark slivers following terrain contours are gone.** They were the sun hitting
  the integer heightmap's risers, recorded as an open look question since M1. With no sun the
  terracing reads as geometry, which is what it is.
- **The low ambient fill is gone with it.** Faces angled away from the sun no longer go nearly
  black; a face's brightness is now the light that reaches it times a `NORMAL`-derived face tint
  — top 1.0, ±Z 0.8, ±X 0.65, bottom 0.5, the cheap fake every voxel game uses.

The face tint is computed in the shader rather than baked into the vertex, and that is a
decision: a quad has exactly one normal so it costs nothing worth counting, while baking it would
put a *look* constant into the merge key, where retuning it means remeshing the world.

### Three screenshots, because one would have proved less

`--skybrightness=` scales the sky channel alone and `--debuglamp` places one lamp without a
keypress, so the look questions are repeatable rather than a person's recollection.

- **Daylight** — skylight, AO and the face tint all read correctly, no slivers.
- **`--skybrightness=0`** — the terrain renders *pure black*. That is three things at once: the
  sky channel is what lights the world, block light really is zero where nothing emits, and
  darkness renders as darkness with no accidental ambient anywhere.
- **`--skybrightness=0 --debuglamp`** — a warm pool falling off over ~15 m across the terraces,
  and nothing else lit. **This is the only shot that can prove `COLOR.g` carries block light and
  is tinted separately**, since every other test in the milestone would pass identically if the
  two channels had been written into the same byte. The gradient is smooth, with no visible
  cell-blockiness at that range — a good early sign for the 1 m cell, which M5d judges properly.

**No ambient floor**: an unlit cave is black, which is DESIGN's intent — M5 supplies the darkness
and M13 is what makes it a hazard. If M5d says it is unusable rather than dark, the lever is one
added constant in the shader, not a change to the grid.

**One thing the shots raise for M5d:** the Environment's fog greys distant terrain regardless of
light, so at `--skybrightness=0` the horizon is fog-coloured while the ground is black. Against a
lit world that reads fine; against a dark one it does not, and a fog that ignores the light grid
will look wrong the first time somebody stands in a big unlit cavern.

### The battery: 13 recipes, 9 caught, and the two real holes were both *joins*

Run after the fact rather than before, which is worth noting because it found things the tests
written alongside the code did not.

**Hole 1 — nothing checked that each corner samples its own vertex.** Making all four corners
sample one shared point left the whole suite green. That is the difference between per-vertex
light and per-*face* light — between a smooth gradient across a surface and flat-shaded facets —
and every existing test survived it: the merge tests still saw shades varying face to face, the
flat-floor test had uniform light so the corners agreed anyway, and the constant-shade-along-a-
merged-run test is satisfied *trivially* when all of a quad's corners are equal.
`EachCornerSamplesItsOwnVertexRatherThanOneSharedPoint` uses a gradient along X against a +Y
face, where corners 0 and 3 must differ.

**Hole 2 — nothing checked that the pipeline hands the mesher any light at all.** Replacing
`GatherLight(chunk.Position)` with `null` at the submission site left 556 tests green. M5c-2's
vertex-light suite calls `GreedyMesher.Build` directly; M5c-1's pipeline suite checks states,
counters and quiescence. **Every part of the path was tested and the join was not**, so the world
could have rendered at flat full brightness with nothing failing.
`ALampPutsBlockLightIntoADeliveredMeshsVertices` asserts on the bytes of a delivered `ChunkMesh`,
which is the last thing Core owns before Godot.

Both holes are the same shape and it is the shape to watch for: **a unit-tested producer, a
unit-tested consumer, and an untested wire between them.** M5c-1 found the same thing one layer
in. Worth a standing habit — when a battery is written, put a recipe on every *call site* that
passes data between layers, not only on the logic at each end.

**Two recipes were caught by nothing and are not holes:**

- **Shrinking the grid to the plan's 10³ leaves everything green**, because `Sample` skips
  zero-weight terms and so never forms the out-of-range index. So the outermost ring is belt and
  braces, M4a's contact-epsilon category — and the honest reason to keep it is that **the two
  mechanisms together are what make this safe**: drop the ring and the zero-weight `continue`
  silently stops being an optimisation and becomes a bounds check, so deleting it as tidying
  walks off the end of the array. The comment claiming the index "still has to exist" was wrong
  and is corrected.
- **Truncating instead of rounding in `Scale` changes nothing measurable.** The two differ by at
  most 1 in 255, below the resolution of every look question and every merge here. Recorded
  rather than defended with a test.

### A test whose guard was the only thing that noticed it

`AMergedQuadStillHasConstantShadeAlongEveryAxisItMergedOn` first used a per-cell pseudo-random
light field. Every face then had a different shade, **nothing merged at all**, and the property
under test was never exercised once — the assertions all passed vacuously. Only
`Assert.True(merged > 0)` failed. Replaced with two flat regions and one boundary.

That is the same lesson as M5c-1's two fixture traps, arriving from the third direction: **the
"this test was not vacuous" guard is not decoration, and on this occasion it was the entire
test.**

---

## 2026-08-30 — M5c-1: the light stage, and light turns out to need no margin ring

Done. Suite **518 → 542**. `GenerationStage.Lit` is a third rung on the barrier;
`ChunkStreamer` implements `ILightChunks` and owns a `ChunkLight` per chunk plus a `SkyColumn`
per (x, z) with its own refcount; the propagation drains on the sim thread under a time slice;
and an edit updates the sky column, queues a derive, and marks for remesh exactly the drawn
chunks whose sampled light moved.

**M5c was split M5c-1 / M5c-2** on the same grounds M2c and M2d were split: everything here is
verifiable headlessly, and nothing in the other half — the trilinear sample, the shader, the
sun going away — is. **A lamp still does not glow. That is M5c-2.**

### Light needs no third margin ring, and it was one decision away from needing one

Following the existing ladder mechanically: a chunk meshes when its neighbours are `Lit`; a
chunk is `Lit` when its neighbours are at `Features`; a chunk reaches `Features` when *its*
neighbours have terrain. Three rungs, so the load margin grows from 2 to 3 — **+17% chunks on
top of M2c's +41%**, paid on every axis, forever.

It is avoidable because **deriving a chunk's light asks for voxels, not for a stage.** The
outermost loaded ring derives its cells and joins the propagation while never reaching `Lit` and
never meshing — which is not a workaround but exactly the arrangement the margin's third
load-bearing reason already describes. `TryLightNeighbours` reads what voxels exist: an
*untracked* neighbour is skipped (it reads as air, it has no grid, so propagation stops at that
face whatever the conductance bit says), and a *tracked* one without voxels blocks (it will have
voxels shortly, and recording a face as conducting against air it is about to fill is a wall
that leaks light for as long as it exists).

**Revisit trigger, and it is a real one: when `ApplyFeatures` starts placing anything, it must
route through the relight path the way `SetVoxel` does.** Deriving at `Terrain` is sound today
only because the feature stage writes nothing. A feature that writes into a neighbour is an edit
by another name, and the machinery to repair it already exists.

### The bug M5b could not have found: a push into a chunk that does not exist yet

The propagation **pushes rather than pulls**, so a push into a chunk with no grid is dropped.
That is correct while the chunk will never exist, and a lost update the moment it arrives three
frames later — and **nothing remembered having tried**. The neighbour that pushed left both
queues long ago, and a new chunk seeding its own sources says nothing about light arriving from
outside it.

The symptom is a chunk lit towards whichever neighbours were derived after it and dark towards
those derived before, so it **moves with worker scheduling**: it showed up as a one-in-six flake
in `UnloadingReleasesTheLightGridAndReturningRebuildsIt`, reported by the fixpoint oracle as
`Cell(11, -1, -8) Sky: holds 14, neighbours and its own source say 15`.

**M5b could not have found it.** Every fixture there builds all of its grids before lighting any
of them, which is a luxury the streamer does not have — chunks arrive in whatever order eight
workers finish. `LightPropagator.SpreadInto` re-spreads from the **six adjacent face layers**
whenever a grid comes into existence; not the whole 488-cell shell, because light crosses faces
only and the twelve edges and eight corners cannot conduct into the chunk at all. Re-spreading
without darkening, because the add pass only writes where it is strictly brighter, so it is
idempotent.

### The first deterministic test for it passed on broken code

A one-in-six detector is not a detector, so the bug was pinned deterministically at the
propagator level. The first attempt dropped the grid of a chunk under a **sky shaft** — and
every cell of an open shaft is *itself* a full sky source, so rebuilding the derived data
re-seeds the whole chunk and it relights with no help from anyone. It passed with `SpreadInto`
disabled.

The shape that separates the arms is **a lamp in the neighbouring chunk, underground**: the
restored chunk then has nothing intrinsic anywhere in it, so every lit cell in it arrived from
outside. Catches the defect in 207 ms, every time.

**Fourth time this project's rule has earned itself, and a new variety of it.** M3a's fuzz hung
instead of failing; M3b's first oracle was wrong in the subject's favour; M3c had a test whose
comment claimed a coverage it did not have; this one was **a scenario that made both arms
agree** — the trap is not the assertion, it is the fixture.

### The plan costed the wrong half of M5, by an order of magnitude

M5-PLAN estimated the initial fill at ~779k cell visits and ~15 ms, and said nothing at all
about **deriving**, which is the larger half. Measured in Release against the shipped seed with
caves on:

```
             copy      derive    total
 surface     0.189     0.056     0.245 ms
 deep        0.177     0.150     0.327 ms
 open sky    0.155     0.049     0.204 ms
```

So ~0.27 ms a chunk and **~0.41 s for the whole 1,521-chunk fill** — against generation's
0.47 ms a chunk, except that this is one sim thread rather than eight workers. The budget is
2 ms a frame, matching the result budget: ~200 frames to light the world, comfortably inside the
load it accompanies.

**The lever, if M5d wants one, is `PaddedChunk.CopyFrom` and not the derivation.** The copy is
60–75% of every row above, and it recomputes the neighbour index per voxel where 27 block copies
would do — which would speed up meshing too, since a mesh job builds one of these per chunk as
well. **Named and deliberately not built**, because raising the budget bought back the whole
regression on its own.

### The budget is 4 ms, and the standing start is what says so

Light delays the *first draw*, because nothing meshes until its neighbourhood is lit — so the
number that matters is not throughput but when the world can show anything. Swept headlessly at
r4h/2v, read at the 5 s report:

```
  1 ms    857 / 1521 derived, 664 queued,   0 drawn
  2 ms   1467 / 1521 derived,  54 queued,   0 drawn
  4 ms   1521 / 1521 derived,   0 queued, 165 drawn, settled
  8 ms   identical to 4 ms
```

**2 ms was a real regression and it was invisible in the tests**, which pass their own generous
budget. 4 ms restores M2d's recorded "settled inside 5 s"; 8 ms buys nothing, because light has
stopped being the constraint. It is **a ceiling and not a reservation** — once the world is lit
nothing is queued and the step costs nothing, so the 24% of a 60 Hz frame it permits is spent
during a load and never afterwards. `--lightbudget=` makes the sweep a shell loop, the fourth
seam of that shape after `--cavestep`, `--applybudget` and `--ao`.

**With it, the headless contract is unchanged to the digit:** 1521 viewed (1521 tracked), 165
drawn, 1926 jobs, 492 quads per drawn chunk. M2d's and M5a's figures, with light now in the
barrier — which is what keeps M5d's measurements comparable with everything before them.

The real-world light numbers, which the test world got wrong in both directions:

```
1521 derives / 1521 tracked (299 uniform, 20%), 585,012 cells visited,
169 sky columns, 0 relight remeshes, light settled
```

- **Exactly one derive per chunk**, in the real world and not just the fixture.
- **The uniform closed form covers 20%**, against 12% measured in the all-surface test world and
  M2b's 68% for chunks considered alone. Requiring the six face neighbours uniform *and equal* is
  what costs the difference.
- **585k cell visits against M5-PLAN's estimated 779k.** The plan's propagation arithmetic was
  good — 25% high — which sharpens the finding rather than softening it: **the estimate it got
  right was not the one that mattered.**
- **Zero relight remeshes during the fill**, which is the priority ordering staying intact. A
  relight that marked undrawn chunks would show up here as a large number and as the whole
  distance-ordered queue arriving at band3.

One optimisation *was* built, because it removes the copy rather than making it faster: **a
uniform neighbourhood has a closed form** — every face conducts iff the material is not opaque,
every cell emits what the material emits — and it needs no voxel copy at all. It is pinned
against the general path cell for cell and face for face, the `IsCaveAt`/`GenerateTerrain` shape
of test, because two derivations of one field is two derivations that can drift.

**Its coverage measured at only 12% in the test world**, which is all surface. M2b's "68% of
chunks are uniform" is about chunks alone; this needs the chunk *and its six face neighbours*
uniform and equal, which is strictly rarer. **A real-world coverage number is an M5d
measurement.** Also worth recording: **light's dependency set is the six face neighbours, not
the mesher's twenty-six** — conductance reads one voxel layer across each cell face and emission
reads only the chunk's own interior, so nothing in the derivation ever looks at an edge or a
corner. That is what makes the uniform case checkable at all: seven chunk headers, not
twenty-seven.

### The break-recipe battery found three holes, and each was a different kind

Twelve recipes, one decision each, against the whole suite. Nine were caught — some loudly:
**"advancing to `Lit` does not push its 26 neighbours" fails 14 tests**, which is M2c-2's rule
being load-bearing in a new place. Three were caught by nothing, and the interesting part is
that no two were the same kind of gap.

**1. `bool urgent = chunk.NeedsRemesh` put back into `SubmitMesh` — 0 failed.** The two-flag
split is the entire mitigation M5-PLAN names, and nothing asserted it. Both existing tests
checked the *sign* of the counters, and with the mutation the relight remeshes merely got counted
as edit remeshes as well, so both stayed non-zero. `OnlyTheChunkHoldingTheLampIsUrgent` asserts
the number: a lamp in the interior of a chunk dirties exactly one chunk by voxels, so
`EditRemeshes` must be exactly 1 however many chunks the light reached.

**2. `(present & ~chunk.LightNeighbours) != 0` weakened to `present != …` — 0 failed.** A
scenario that makes both arms agree: on a still world the mask never moves and the two rules are
the identity. Two overlapping viewers is the shape that separates them — removing the second
unloads its exclusive chunks, so every chunk left standing near that boundary *loses* a
neighbour and none gains one. `LosingANeighbourReDerivesNothing`.

**3. `QueueColumnLightBuild` reduced to the edited chunk alone — 0 failed, and `AShaftDugFromTheSurfaceCarriesDaylightToItsFloor` did not notice.** This one is the subtlest.
That test digs a **one-voxel bore**, and a one-voxel bore never makes any cell a sky source,
because a cell is open to the sky only when **all sixteen** of its voxel columns are. Its light
arrives by free fall from above instead — which conductance alone can give and take away, so the
sky-height half of the rebuild never mattered to it.

A **cell-wide** shaft is the shape that separates them: every cell down it is a sky source in its
own right, so capping the top has to clear the source flag in chunks nobody edited. Rebuild only
the edited chunk and the shaft below sits at full daylight with a rock cap over it.
`CappingACellWideShaftPutsTheDarkBackInChunksTheCapIsNotIn` asserts the deep cell is in a
different chunk from the cap, so it cannot quietly stop covering the thing it exists for.

**M5-PLAN warned about exactly this and the warning was not enough.** It says the fuzz "must
include edits that break through a column's topmost opaque voxel, or the sky-height path never
runs and it passes for the wrong reason". The path *did* run — the column moved, the sky flags
were rebuilt — and the test still passed for the wrong reason, because the flags it rebuilt were
never the ones carrying the light. **A warning about which code path runs is not a warning about
which property is being checked.**

One recipe's patch string did not match and was re-run separately. And
`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` failed in one arm and
passes on a clean tree — M2a's known flake, already recorded, not M5's.

### Two smaller things worth not rediscovering

**A derive happens off the work list, so it has to hand the chunk back.** `BuildLight` runs from
its own budgeted queue and the chunk it just made eligible for `Lit` is not otherwise looked at
again. Missing that push stalls the pipeline with every grid built, the propagator idle, and no
chunk ever meshing — M2c-2's rule in a new costume.

**`IsQuiescent` moved onto the pipeline**, because M5c added two queues to the definition. The
test harness settled on "no jobs, empty work list", which after this change reported quiescence
while every chunk sat one stage short of meshing. Not a wrong answer so much as an out-of-date
one — and it is exactly what a stalled light stage looks like from outside.

**The suite went 8 s → 83 s → 16 s.** Deriving is real work and `Settle` sleeps a millisecond a
pass, so a production-sized light slice turned every pipeline test into a few hundred extra
round trips. `PipelineHarness.GenerousLightBudget` is the fix, and a test that actually wants a
starved budget passes its own. The cheap-fixtures rule, paid again.

---

## 2026-08-30 — Settle three things M5c's plan stopped one step short of

No code. Three decisions recorded before M5c starts, in the same shape as the four settled
before M5a. All three were found by reading M5b's `LightTestWorld` next to `ChunkPipeline`,
which is the pairing that fixture was written to invite — its doc comment says outright that its
edit path *"is deliberately the one M5c has to implement"*.

**None of the three is a reversal.** Each is a place the plan stated something confidently and
stopped one level above where the wrong answer was sitting. Same shape as M5a's UV2 packing:
the plan expressed a preference, and the reason it was impossible lived one level below where
the plan had looked.

### 1. Nothing said what sets the `Lit` stage, and the obvious answer is wrong

The plan says light is a third `GenerationStage` and `CanMesh` gains one clause. What it skips
is that **a global BFS has no per-chunk completion event.** `Features` is set by the sim thread
finishing a call for that chunk; light has no equivalent moment, because the queue is one queue
over the loaded region and a frontier crosses chunk boundaries without noticing them.

A chunk's light is final when two things hold, and only one is per chunk:

- **Structural** — every chunk within light's reach has derived data. 15 cells is just under two
  chunks, so that is the chunks within two, which inside the visible box is exactly what the load
  margin guarantees. **The margin's third load-bearing reason, cashed in.**
- **Temporal** — the propagation has actually run. Global, because the queue is.

**Settled: a chunk advances to `Lit` when its derived data is built and the propagator has no
work outstanding** — a per-chunk stage with one global term in its condition.

**Rejected: a per-chunk count of how many of its cells are queued.** It is the obvious way to
make the temporal half local, and it is wrong in the direction that does not show up in testing —
a chunk with no queued cells of its own can have a neighbour's frontier one step from spilling
in, so the count reads settled while the light is still moving. It would have passed every test
worth writing and produced a dark seam that moved with the player.

The global term is not the stall it looks like. Estimated, and all three numbers are estimates:
~779k cell visits for the initial fill at r4h/2v, ~15 ms, ~15 frames under a 1 ms budget, against
the ~0.22 s the spawn gate already costs. In flight a new chunk column is a few chunks of light;
a swing is a handful of cells, and most swings change none at all. **M5d measures the first.**

**What makes the coarseness safe is that being wrong is recoverable by a path that has to exist
anyway.** Light changing in an already-meshed chunk is caught by the drain's changed-cell set
marking a remesh — the same mechanism the edit path needs, so streaming and mining share it and
neither is a special case of the other.

### 2. The padded light grid cannot be gathered the way the padded voxel copy is

The plan says `MeshChunkJob` "takes a second neighbourhood array; nothing else about it changes".
**It cannot: a `ChunkLight` reference is not safe to hand a worker.**

Holding 27 `VoxelChunk` references is safe for a reason that is M3a's rather than M2a's —
**voxels are copy-on-write.** A chunk handed to a job is immutable from then on, and an edit that
finds the flag set clones instead of writing. Light has no such rule and cannot cheaply have one:
the propagator writes into these 512-byte arrays continuously and in place, *including while a
mesh job is running*, and giving light copy-on-write would mean a clone every time a drain
touched a chunk — thousands per drain — against one 1,000-byte copy per mesh job.

**Settled: the padded light grid is built at submission, on the sim thread**, and the job carries
a value instead of references. 1,000 bytes against the 157 KB padded voxel copy the worker still
builds for itself — **0.6%**, and it removes the race rather than managing it.

Recorded prominently because it reads as an inconsistency with `PaddedChunk` and invites being
"fixed" back. The two are built in different places for reasons that do not transfer either way.

### 3. `NeedsRemesh` means two things, and M5c is what makes them come apart

The plan's cheap mitigation — a light-only remesh at ordinary priority — is written as "one
line, `SubmitMesh` already distinguishes the two". **It does not.**
`StreamedChunk.NeedsRemesh` carries both *the mesh is stale* and *the player is waiting on it*,
and `SubmitMesh` reads the single flag for both. That is exactly right while an edit is the only
thing that can invalidate a mesh, and it stops being right the moment a relight is the second
thing that can.

**Settled: `NeedsRemesh` keeps the first meaning and a new `RemeshUrgent` carries the second.**
An edit sets both, a relight sets only the first, `SubmitMesh` clears both.

**And a relight only marks a chunk that has actually drawn something.** A chunk that has never
meshed needs no marking: the barrier already holds it until its light exists, and marking it
would make its *first* mesh an urgent one. During the initial fill every chunk's light changes,
so that arrives the entire distance-ordered streaming queue at band3 and M2a's ordering is
silently gone. **Nothing would look broken — the world would just load in the wrong order**,
which is the failure mode this project has learned to distrust most.

### The pattern worth keeping

All three came from the same act: reading the test fixture from the previous part against the
production code it was modelled on. M5b wrote `LightTestWorld` as a statement of what M5c has to
build and said so in a comment. Doing that reading *before* writing M5c's code, rather than
discovering the mismatches one compile error at a time, is what turned three defects into three
paragraphs.

---

## 2026-08-30 — M5b: the light grid, and an oracle sharper than the one the plan named

Done. Suite **496 → 518**. `MinerCraft.Core/Lighting/` holds `LightCellPos`, `ChunkLight` with
cell-face conductance, `SkyColumn`, and `LightPropagator` — the add pass, the un-light pass and
one invalidation that repairs any edit. `MaterialDefinition.LightEmission` is projected into
`MaterialTable`, `mods/minercraft/materials/lamp.json` ships a full-strength emitter, and **L**
places one.

Nothing is wired into the pipeline yet — that is M5c. A lamp placed today is a pale block that
does not glow.

### The oracle is better than "incremental equals from-scratch", and by a category

M5-PLAN's oracle is that after any edit sequence the maintained grid equals a from-scratch
flood. That is a good test and it is here. But it **runs the same add pass as the thing it
checks**, so the only thing it can independently catch is the un-light pass.

The invariant is checkable *locally*:

```
V(cell) = max( S(cell), max over conducting neighbours n of ( V(n) − attenuation(n → cell) ) )
```

That fixpoint is **unique** — a cycle of equal values would need zero attenuation all the way
round, and the only zero-attenuation step is full skylight falling downward, which strictly
decreases Y — so satisfying it cell by cell *is* correctness, with no propagation anywhere in
the check. `LightTestWorld.AssertFixpoint` walks every cell of every chunk and re-derives the
attenuation rule from scratch rather than calling the propagator's.

Same category as M4a's sealed room, and found the same way: by asking what property of this is
a boolean over a constructed situation rather than an agreement between two implementations.
A third one fell out for free — **the incrementally maintained sky heights must equal a fresh
downward scan of the voxels** — which is what `AssertSkyHeightsMatchAScan` checks.

### The defect, and it explains seven failures at once

`Darken` reset a cell to its own source value and queued the un-light walk when the value had
*fallen*. It did nothing when the value had **risen**. So placing a lamp wrote emission 15 into
the derived data, and left the cell's propagated value at the zero it held as empty air — a lamp
that lit nothing, a shaft that stayed dark on breakthrough, and the from-scratch oracle
disagreeing on every seed. One line, seven tests.

Worth naming because the shape recurs: the invalidation is symmetric in intent — *the cell's
value must end at its own source, whichever way it moved* — and the code had only written the
half that the un-light pass made obvious.

### Two break-recipes came back green, and they were different problems

Twelve recipes run. Ten bit. The two that did not were both real, and neither was a missing test
in the ordinary sense.

**The six-neighbour expansion in `Invalidate` was provably dead work.** It existed for the
closing-face case: when the face between A and B closes, a walk starting at A never examines B,
because the face it would cross no longer conducts, so B keeps light nothing will remove.
Removing the expansion failed nothing — and cannot, because **conductance is recorded on both
cells of a face**, so a face that changes always reports *both* of them in
`BuildDerived`'s changed set. The far side is already invalidated by the caller's ordinary
discipline. Deleted, the contract written into `Invalidate`'s doc comment, and the property it
depends on pinned by `SealingAFaceReportsBothOfItsCells` — because nothing else would notice it
stopping being true. Breaking the reporting instead fails 11 tests, which is what makes the
one-cell version safe rather than lucky. This is the fourth resolution from M4c/M4d: *impossible
by construction, because something elsewhere already rules the case out.*

**"Let any skylight value fall unattenuated" was a genuine hole**, and the reason is worth
keeping. `AShaftStaysFullyLitHoweverDeepItGoes` could not distinguish the two rules, because a
shaft straight down from the surface carries *full* skylight, which falls free under both. The
case that separates them needs light that is already dim: a horizontal branch off the shaft (10
at its far end) with a **second shaft dropping from there**. Correct rule, it attenuates 10 → 9 →
8; broken rule, 10 forever, and every dimly lit patch in the world hangs a column of its own
light down to bedrock. The test now builds that shape and the recipe fails.

**And one recipe hangs rather than failing.** Dropping the "only if greater" guard in the add
pass lets a cell be re-enqueued at an equal value, and the queue never drains — exactly what
M5-PLAN predicted, and exactly the M3a lesson about a test that deadlocks on the bug it exists
to catch. The battery treats a timeout as a distinct outcome so it reads as "caught" rather than
as infrastructure trouble.

### The test fixture was the other half of the problem, and its shape is M5c's

The first run took **4.5 minutes and ended with the test host crashing**. None of that was the
engine:

- `LightingTests.Wall` built a world-spanning plane with 16,384 single-voxel *incremental* edits,
  each repairing the light around itself. Setup is now written raw and the incremental
  wall-closing test seals a corridor instead — sixteen voxels rather than sixteen thousand.
- `LightTestWorld` allocated a fresh `PaddedChunk` — 34³ ints, 157 KB — per chunk per edit. Now
  one reused buffer. That was most of the memory pressure behind the crash.
- **The rebuild scope was the union of two different shapes, and separating them is a ~40×
  saving.** A voxel's *conductance* reaches only its own chunk, plus a neighbour across whichever
  faces it actually touches — so an interior voxel, 30 in 32, rebuilds exactly one chunk. A
  change to the column's *sky height* is not local at all: it moves the sky flag on cells the
  whole way up and down that chunk column, and nowhere else. The first version rebuilt the 3×3
  horizontal footprint over the full loaded height every time.

Six seconds now. **M5c should implement exactly those two scopes** — the fixture is deliberately
written as the thing M5c has to build.

### Smaller things worth keeping

- **The derived side is 1.5 KB per chunk, not M5-PLAN's 1 KB**, and the extra is not optional.
  The plan budgeted 512 bytes of values plus 512 of conductance; the two *intrinsic source*
  fields — the sky-source bit and the emission nibble — are what let the un-light walk zero a
  cell without erasing the lamp it is walking away from. Still nothing against ~46 KiB of mesh.
- **A cell is seeded with skylight only if *every* one of its sixteen voxel columns is open**,
  not any. Under-seeding is recoverable — the cell above is a source, the face between them
  conducts through the open half, and full skylight falls through it for nothing — so a cliff
  face is lit by propagation anyway. Over-seeding is not recoverable: the value is per cell, so
  a mostly-solid cell declared a source pushes daylight into the rock through its own faces. On
  flat and terraced ground the two rules agree to within a voxel.
- **`ISurfaceHeights` exists so a test can state a flat world in one line** rather than reverse
  engineer a seed that produces one. `WorldGenerator` implements it and nothing else does.
- **`SkyColumn`'s downward scan is bounded by the loaded region, not by its own limit**, because
  an unloaded voxel reads as opaque — the same rule the collider uses, for the same reason. The
  explicit `MaxScanVoxels` is a backstop for a fixture that answers "air" forever, and it is
  labelled as such rather than as a correctness bound.
- **`Conducts` uses `VoxelFace`'s table rather than restating the six directions.** Conductance
  *is* a statement about voxel faces, and two tables of the same six directions is two tables
  that can disagree. Lighting now depends on the meshing namespace for that one table; if that
  ever reads badly the fix is to promote `VoxelFace` to `Voxels`, not to copy it.

### Noticed in passing, not fixed

`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` failed in **two** of the
twelve break-recipe runs and passes on the clean tree. It is intermittent, it is M2a's, and it
made two recipe results ambiguous until the lighting failures were read separately from it. **A
flaky test is worse than a missing one during a mutation battery**, because "1 failed" reads as
"caught". On the TODO.

## 2026-08-30 — M5a: vertex AO, the four-corner merge key, and the number it was built to take

Done. Suite **468 → 496**. Per-corner ambient occlusion from the padded chunk, two shade
channels in the vertex, `CornerShade` in the merge key, and the diagonal flip. `--ao=0` restores
the pre-M5a constant key, which is the A/B this part exists to produce.

### The number

From a **standing start**, so the chunk set is deterministic and the two runs compare two
numbers rather than two samples of a moving flythrough:

```
r4h/2v, 1521 viewed, 165 drawn        AO off      AO on     ratio
quads                                  43 340     81 331
quads per drawn chunk                     262        492    1.88x

r12h/3v @ 6 m/s, 1543 drawn           AO off      AO on
quads per drawn chunk                     253        472    1.87x
static memory                         94.8 MiB  150.4 MiB   1.59x
update ms  p99 / max                1.39 / 2.32  2.05 / 3.77
main thread                             1.26%      1.66%    of one core
```

**Measured 1.88x, against a plan that predicted 2–4x.** The estimate ran pessimistic, which is
now the standing pattern for every guess in this project. Two things follow:

- **The AO-alone baseline for M5's fragment-shader trigger is 492 quads per drawn chunk** at
  r4h/2v. The recorded trigger is light in the merge key costing more than **2x the quads of AO
  alone**, so M5c must stay under ~984. That number was not available until now and is half of
  the decision a milestone early, exactly as planned.
- **Mesh memory grew 1.59x, not 1.88x**, because a chunk's memory is not all mesh. Solving the
  two r12 runs for a constant baseline gives **~42 KiB of mesh per drawn chunk with AO off**,
  which independently reproduces M2d's separately measured ~46 KiB, and ~79 KiB with it on.
  Extrapolated to r12's 2,245-chunk case that is ~205 MiB against the plan's 200–400 MiB
  estimate — the low end was right.

Streaming is unchanged where it should be: **1521 viewed / 165 drawn / 1926 jobs**, M2d's
recorded figures to the digit, and 174 drawn on the 40 m/s flythrough both ways.

### Where the two bytes live, and the plan's reason for choosing was not the real one

M5-PLAN said to verify before writing the mesher: the default was Godot's vertex `COLOR` array,
the alternative packing both bytes into UV2.y as `sky x 256 + block`, "exact in float32 and
**not** exact in float16". Two probes were run against Godot 4.7.1 rather than assumed:

- `AddSurfaceFromArrays` with no flags produces a surface **without**
  `ARRAY_FLAG_COMPRESS_ATTRIBUTES`. UV2 stays float32 and the packed value round-trips exactly.
  Colours round-trip exactly at 8 bits too.
- A render probe — two quads under one `ShaderMaterial`, one taking `ALBEDO` from `COLOR` and one
  from `UV2.y`, carrying the same value — came back one output byte apart. That is 8-bit
  quantisation of the stored value, not an sRGB decode, which would have been ~0.23 apart. **No
  colour-space conversion is applied to `COLOR` under a `ShaderMaterial`.**

So both options worked on precision, and the plan's stated tie-break said to pack and save four
bytes per vertex. **Packing is wrong anyway, for a reason precision does not reach: the hardware
interpolates the packed number.** `unpack(lerp(pack(a), pack(b)))` is not `lerp(a, b)` — the
fractional part of an interpolated sky channel lands in the block channel's byte, so two corners
one sky level apart sweep block light through its entire range in between. Per-vertex shade
exists *to be interpolated*; a packed pair of independently varying channels cannot be. Four
8-bit channels in an attribute have no such coupling.

**`COLOR` it is** — `R = shadeSky`, `G = shadeBlock`, two spare — and the decision is now
structural rather than a four-byte trade.

The probe that nearly lied is worth recording: the first render probe sampled pixels at x=16 and
x=48 of a 64x32 viewport and reported "COLOR is not a passthrough". A Camera3D's orthogonal
`size` is its **height**, so the two 1-unit quads only covered the middle half and one sample
landed on Godot's default background — which is ~0.298, close enough to a real answer to be
believed. Watch the probe fail too, not just the test.

### The flip compares total shade, not AO

M5-PLAN states the rule as `ao[0] + ao[2] > ao[1] + ao[3]`. Implemented on the **sum of both
shade channels**, which is the identical rule at M5a — block light is zero and the sky channel is
AO-monotone — and the right one afterwards. At M5c a flat floor with a torch on it has constant
AO and a varying shade gradient, and an AO-only test would never fire on the surface most likely
to show the crease the flip exists to remove. The comparison stays a pure function of the merge
key either way, so every face merged into a quad agrees on which diagonal that quad takes.

### Merging is exact, and there is a sharper way to say it than the plan had

The plan's claim is that two faces merge iff all four corner shades match, and that the merge is
then exact rather than approximate. The mechanism is worth writing down because it is what makes
the claim *checkable*: adjacent faces share two corners, and an independently derived shade at a
shared corner is the same number by definition — so `A.corner1 == B.corner0` holds for free, and
requiring `A.corner0 == B.corner0` therefore **forces `A.corner0 == A.corner1`**. Key equality
does not merely permit a constant shade along the run, it implies one. A merged quad interpolates
exactly what the separate quads would have, with nothing left to approximate.

That gives a test the plan did not name and which is stronger than an assertion:
`AMergedQuadHasConstantShadeAlongEveryAxisItMergedOn`.

### Break-recipes, and the two that were not what the plan expected

Seven mutations applied and the suite run against each:

| Recipe | Tests failed |
|---|---|
| Swap `side1` and `side2` | **0** |
| Swap the corner sign table (relabels corners 1 and 3) | 8 |
| Drop the `corner` term | 4 |
| Drop the `side1 && side2` case | 5 |
| Sample the diagonal on one axis only | 6 |
| Remove `CornerShade` from the merge key | 8 |
| Force the diagonal flip off | 6 |
| Wind the flipped triangles backwards | 2 |

**The plan's "swap `side1`/`side2`" recipe is a no-op, not a hole.** `(s1 && s2) ? 0 : 3 - (s1 +
s2 + c)` is symmetric in its two sides — both `&&` and `+` commute — so the mutation cannot
change any output. The meaningful version of that recipe is mislabelling *which corner* gets
which sample, which is what swapping the sign table does, and 8 tests catch it. This is the third
category the working rule names: unreachable at these parameters rather than untested.

`Wind the flipped triangles backwards` was added because the existing
`EveryQuadIsWoundConsistentlyWithItsNormal` reads vertex **positions** only, so the flip — which
changes indices and nothing else — structurally cannot make it fail. M5-PLAN says "if that test
can be made to fail by the flip, the flip is wrong", and it cannot be, which is the right answer
for that test and a hole for the flip. `BothTrianglesOfEveryQuadFaceOutwardsInEitherFlipState`
walks the actual indices and asserts a flipped quad was seen, so the branch cannot go untested.

### A test whose comment had never been true

`ChunkPipelineTests.AnEditIsWrittenAndTheChunkIsRemeshed` failed, and the pipeline was fine.
Its comment said the carve target was "somewhere buried, so removing a voxel actually opens a
cavity" — but `FindSolidInterior` meant "not on the chunk boundary" and returned the first solid
voxel in scan order, which sits at a terrain-or-cave boundary with air on three sides. Carving it
removes three of its own faces and exposes three on its neighbours: **a change of exactly zero
quads.** The face-count assertion passed anyway until M5a shifted how the surrounding quads
merged and the two totals came out equal at 1,320 indices.

`FindSolidInterior` now checks all six neighbours. Same shape as M3c's pipeline test whose comment
claimed it caught the border rule — **the fixture had never set up the situation it described**,
and only an unrelated change made it say so.

### Verified on screen

Headless has no renderer, so a shader error would not have surfaced. A windowed profile run
reports **209 draw calls, 186,728 primitives** with no shader errors, and `screenshot.gd` — kept,
because M5d's questions are look questions and this makes them repeatable — captures the real
scene from the real spawn. The A/B is unambiguous: with AO off the heightmap's terraces read as
faint colour bands; with it on each riser has a dark contact line at its base and the terracing
reads as geometry. Whether the ramp is *right* is still Matthew's call.

One more datum for a long-open question, taken in passing rather than acted on: windowed
`frame ms` came back at mean 6.939 against headless 6.900 — the same paced main loop, this time
paced by vsync. The headless frame-time trap, fifth sighting.

## 2026-08-30 — M5's four decisions settled, and one of them moved work earlier

All four open decisions from yesterday's plan are closed **before any code**, which is the point
of having planned them. Three cost nothing; the fourth turned out to change what M5a builds.

| | Settled | Revisit trigger |
|---|---|---|
| Light cell size | **1 m.** DESIGN's number stands | M5d plays a `hand_pick` tunnel and it reads blobby across a bore narrower than one cell |
| `SkyColumn` | **M5b**, split: rules in M5b, lifetime in M5c | — |
| Directional light | **Gone.** The grid is the light | — |
| Emission range | **0..15**, matching the field with no scaling | *"unless we come up with better lighting later"* |

### The one that was not free

**Settling the directional light forced a second light channel into the vertex, and that lands
in M5a rather than M5c.**

With no sun, the grid is the only light. And DESIGN's M7 already needs skylight *on its own* —
*"wet-surface shader term driven by skylight × recent rainfall"*. A term that multiplies by
skylight cannot be handed `sky × block × AO` pre-multiplied; the skylight is gone by then. So the
vertex carries two values:

```
shadeSky   = skylight   × AO        // 8 bits
shadeBlock = blocklight × AO        // 8 bits
```

**AO multiplies into both**, because AO is geometric darkening and geometry does not care where
the photon came from. Two dividends fall out of that shape:

- **A diurnal cycle is a uniform on `shadeSky` and costs no repropagation.** The alternative —
  scaling the skylight seed and reflooding — relights the world every frame. Nothing asks for
  day and night yet; this is recorded so nobody builds it the expensive way when something does.
- **AO never reaches the shader as its own term**, so no later lighting change can accidentally
  apply it twice.

The merge key becomes `(Material, ulong CornerShade)` — four corner pairs at two bytes each. At
M5a `shadeSky` is full and `shadeBlock` zero everywhere, so only AO varies, which is the same
"in the key from the start" pattern M1c used for light and AO in the first place.

**One thing to verify before writing the mesher, and it is a real trap.** Default home for the two
bytes is Godot's vertex `COLOR` array — four 8-bit normalised channels by definition, no packing,
no precision question, 4 bytes per vertex against a quad's ~184. The alternative packs both into
UV2.y as `sky × 256 + block`, which is exact in float32 and **not** in float16 — and
`ChunkMeshConverter` calls `AddSurfaceFromArrays` with no flags, so whatever Godot 4.7 compresses
UV2 to is what it gets. Packing failing is a silently wrong colour ramp rather than an error.
Either way non-negotiable #4 holds, and the M0 reservation did exactly its job: nothing
downstream assumed the channel, so moving it is one line in two files.

### The other three

**Light cells stay at 1 m, so `--lightcell` is not built.** Same rule as every named fallback
here: the flag existed to answer a question and the question is answered. `LightCellSize` keeps
its power-of-two-dividing-32 constraint, which is correctness rather than tuning — a chunk
boundary must be a cell boundary or independently lit neighbours disagree. Worth restating why it
was a question at all: DESIGN reasons in **metres** — *"light is low-frequency, nobody sees the
difference, 64× cheaper"* — and the number that decides whether it *looks* right is in **voxels**.
A 1 m cell is 64 voxels, the tunnels are 0.75–1.25 m across, and the cell is therefore wider than
the corridor. That is why the trigger is a play rather than a number.

**`SkyColumn` is confirmed for M5b, and split across two parts rather than one.** Its rules —
seed from the heightmap, lower on an edit, query a column — are a pure function and belong with
M5b's fuzzable half. Its *lifetime* is not: a horizontal refcount that survives vertical unload is
streaming state, and it goes in M5c. Keeping them together would put the one mutable,
lifetime-bound thing in M5 inside the part whose whole value is being a pure function with a sharp
oracle. Two notes came with the confirmation:

- **It is a second refcount, on a different axis and a different object.** M2c recorded "don't add
  a second refcount to fix the margin"; this is not that. It is a column-lifetime problem the
  chunk refcount cannot express, because the entire point is surviving the vertical unload the
  chunk refcount performs.
- **The oracle has to reach it.** The incremental-equals-from-scratch fuzz must include edits that
  break through a column's topmost opaque voxel, or the sky-height path never runs and the
  comparison passes for the wrong reason. That is the "instrument with denominators" failure mode
  in a different costume — a green test from a path that was never taken.

**Emission stays 0..15.** It matches the field exactly and needs no scaling, and the cost is that
the brightest lamp any mod can define reaches exactly 15 m — for a *storage* reason rather than a
design one. A game-balance number wearing a storage costume, the same shape as terminal velocity
being a streaming parameter in a physics costume. The trigger is Matthew's own: better lighting
later, meaning a wider field or coloured light, takes the range with it.

### What closed elsewhere

The directional light going retires two items that have been open since M0 and M1: **"ambient fill
is low — faces angled away from the sun go nearly black"**, and the **bright and dark slivers
following terrain contours** in M1's screenshots. Both are the sun striking the integer heightmap's
risers. With no sun the terracing goes back to being geometry rather than a lighting artefact, and
what stays a look question for M5d is whether the face-tint constants that replace directional
shading are right — not whether the sun goes.

DESIGN's Lighting section carries all of it now, because it is a renderer decision and not a
milestone detail.

## 2026-08-30 — M5 planned: the first thing here that is not a function of one chunk

[docs/M5-PLAN.md](docs/M5-PLAN.md), split M5a–M5d, nothing built. Every job this engine runs is
a pure function of one chunk and its immediate neighbours — worldgen of seed and position,
meshing of a 34³ padded copy, a carve of a sphere smaller than a chunk. **Light is the first
thing that is not.** It propagates arbitrarily far, and removing one voxel can change light in a
chunk that is not a neighbour. That is not a mesh job's shape, and pretending it is produces the
classic voxel bug: a dark patch nobody can explain, that only fixes itself when the chunk
happens to unload and come back.

### The number that makes it tractable, and it is exact rather than lucky

4-bit light is 0..15, minus one per metre, so it reaches **15 m**. The load margin is 2 chunks,
which is **16 m**. A source outside the loaded region is at least 16 m from the visible box and
contributes `max(0, 15 − 16) = 0`.

So light computed over the loaded region is **exact** inside the drawn one — not approximate,
exact, with 1 m to spare. Three consequences worth recording before anything is built:

- **The margin now has a third load-bearing reason** — the mesher's padded ring, the cross-chunk
  feature stage, and light reach — and light is the tightest of the three. M2d already
  established the margin is not a tuning knob; it is now less of one.
- **Revisit trigger: 5-bit light reaches 31 m and would need a 4-chunk margin.** Written down
  because the two numbers live in different files and nothing would fail to connect them.
- The margin also happens to be the right region to *compute* light over. Not the visible box —
  light flows inward from up to 15 m outside it.

### Where the parts split, and why AO goes first

| Part | What it is | The risk it removes |
|---|---|---|
| M5a | Vertex AO and the merge key | Greedy meshing collapsing towards culled meshing, found later when AO and light are both suspects |
| M5b | The light grid, propagation, the un-light pass | An incremental update that disagrees with a from-scratch one |
| M5c | The pipeline: light stage, mesh-time sampling, relight on edit | A swing that relights and remeshes 64 chunks, five times a second |
| M5d | Feel and measurement | 1 m cells being too coarse for a 0.75 m tunnel, found after everything is built on them |

**DESIGN lists lighting before AO and the plan reverses it.** AO is derived from voxel geometry
alone, needs nothing from the light grid, and `PaddedChunk.CopyFrom` was built to feed it — M1c's
comment says so in as many words. It also forces the one structurally expensive edit in the
milestone, the merge key. Doing it alone means the quad-count measurement has **exactly one
cause**, and that measurement is half of the biggest decision M5 makes, available a week early.

**M5c is where the surprises are**, and unlike M4 that is also where they are expected: a
not-per-chunk algorithm meeting a per-chunk pipeline, a job scheduler and an edit path that
fires five times a second.

### Four findings from the planning pass

**Sampling light per vertex puts it in the merge key, and a merged quad only interpolates its
four corners.** A quad spanning three light cells ignores the middle one. So the rule is: two
faces merge iff all four corner shades match — and when they do, the merged quad's interpolation
is *identical* to the separate quads'. Merging stays exact. Where light varies, quads stop
merging, and that price is what M5a and M5c measure. The named fallback is a per-chunk 3D
texture sampled in the fragment shader, where **a light change costs no remesh at all** — the
expensive coupling, removed. It is deliberately not built first: it contradicts a DESIGN line
that has held since week one, it needs a material per drawn chunk or a clipmap volume, and every
named fallback in this project so far has proved unnecessary when measured. **Trigger: light in
the key costs more than 2× the quads of AO alone, or breaking into a lit space costs more than
~16 remeshes at a rate mining can sustain.** The reserved UV2.y channel means the vertex format
does not change either way, because AO is per-vertex regardless.

**A 1 m light cell holds 64 voxels, and `hand_pick`'s bore is 0.75 m.** The tunnels this game is
about are *narrower than one light cell*, which means "is this cell opaque" has no good binary
answer. Any-opaque-wins and torches light nothing; any-air-wins and a 0.25 m wall leaks, and
0.25 m is a normal wall thickness here. So opacity is a property of the cell **face**, not the
cell: light crosses iff some voxel pair across the shared 4×4 voxel boundary is open on both
sides. Six bits per cell, derived once from the same padded copy meshing already takes. The
honest residue is that the *value* is still per cell, so rock within 1 m of a lit tunnel glows
slightly; sampling half a cell along the normal hides most of it.

**The vertical radius means there is no top of the world to flood skylight down from.** Every
voxel game that propagates skylight from above loads full columns; this one loads 16–24 m
vertically, and that is a measured, locked decision. Seeding is analytic instead, off
`WorldGenerator.SurfaceHeightAt` — already pure, already exact for unedited terrain — plus a
per-column `SkyColumn` map that edits lower, refcounted on the *horizontal* footprint so it
survives the vertical unloading that happens constantly while descending. ~2 KB per column,
338 KB at r6h. It is also exactly the structure M7's rain occlusion needs, and the one lighting
structure M6 must persist, because light itself is derived data and a column's sky height is
not.

**The oracle is the sharpest available in this milestone: incremental == from-scratch, cell for
cell, after any edit sequence.** No reference implementation to get wrong — from-scratch
propagation *is* the definition, it is merely too slow to run per frame. No tolerance, no
sampling, just an array comparison. Same category as M4a's sealed room, and the reason the
algorithmically fiddliest thing in the project so far is genuinely tractable. **Watch it fail**
by dropping the re-seed step of the removal pass; if it survives, the fuzz is not overlapping
its sources.

### Costs and estimates, all labelled

| | | |
|---|---|---|
| Light grid | 512 cells/chunk at 1 m, **512 bytes** | against ~2 KB voxels, ~46 KiB mesh |
| Conductance | another 512 bytes/chunk | 49,152 pair tests per chunk, once |
| `SkyColumn` | ~2 KB per column, 338 KB at r6h | horizontal refcount |
| Padded light grid | 10³ = 1,000 bytes | −1…8, because a corner vertex sits at cell 8.0 |
| Initial fill | 779k cell visits at r4h/2v | **guess** ~20 ns each → ~15 ms → ~0.25 s budgeted |
| AO quad count | **guess** 2–4× | mesh memory follows; r12's 100 MiB → 200–400 MiB |

The quad-count guess is the one that matters and it is the first thing M5a measures. Every
guessed number in this project so far has run pessimistic, so the honest expectation is that it
comes in low — but AO varies at every edge and the integer heightmap's terracing puts an edge
every few voxels, which is the terrain that would make it come in high instead.

### Four open decisions, recorded rather than settled

- **Light cell size: 1 m (DESIGN) or 0.5 m.** DESIGN reasons in metres — "light is
  low-frequency, nobody sees the difference, 64× cheaper" — and the number that matters is in
  *voxels*: a 1 m cell is 64 voxels, and the tunnels are 0.75–1.25 m across. 0.5 m costs 4 KB
  per chunk against 512 bytes, still nothing beside a 46 KiB mesh, and 8× the propagation. Ships
  as DESIGN's 1 m with `--lightcell` as the flag, so M5d answers it with a screenshot rather
  than an argument. If 0.5 m wins, DESIGN's line changes and the plan is where the reason lives.
- **`SkyColumn` in M5 or M7.** ~150 lines, and it is the difference between "dig a shaft and
  daylight follows you down" being true and being a limitation. The plan builds it at M5b.
- **Does the directional light survive?** Proposed: no — per-face constant tint × light × AO,
  unshaded, which is also what retires the open "ambient fill is low" item and the terracing
  slivers M1's screenshots recorded, since those are the sun hitting integer heightmap risers.
  Judged at M5d.
- **Emission range 0..15** matches the field exactly and needs no scaling, but it also means the
  brightest possible lamp reaches 15 m — a **game-balance number wearing a storage costume**,
  the same shape as terminal velocity being a streaming parameter in a physics costume. Worth
  noticing before a mod author builds against it.

### One scoping fact that shapes M5d

**Nothing in the game can place a light source until M6's placement exists.** So M5b adds
`MaterialDefinition.LightEmission`, one emissive material in `mods/minercraft/` — because the
base game is a mod and a light source is content — and a debug bind on the existing `SetVoxel`.
One key, one material, no inventory, and it should stay embarrassing enough that nobody mistakes
it for the feature.

`ShippedContentTests` gains the matching numeric check, on the M3c principle that a format
having a field is not the same as the content using it meaningfully.

## 2026-08-30 — M4d: the measurement half, and the answer is no

**Can the player outrun the streamer? No — not at any shipped setting, with 2.25× of measured
headroom.** That is the question M4 has been asking since it was planned, and it now has a
number instead of an argument. M4 is done.

`--fall` drops the body from height and measures what falling costs; `--terminal` and
`--fallheight` make the sweep a shell loop. `MoveResult.BlockedByUnloaded` is the signal M4b
deliberately did not build, built now that there is a profiler to consume it. Suite **461 → 468**.

### The headline, measured

Terminal 40 m/s, 250 m falls that actually land, 20 s window:

```
                        r4h/2v            r6h/3v
update ms  p99           1.047             1.541
           max           4.568             5.502
main thread              1.14%             2.27%   of one core
step ms    mean          0.015             0.013
           max           0.347             0.570
substeps / step          3.42              3.41
voxel reads / step       13.3              13.3
held on unloaded world   0 (0.00%)         0 (0.00%)
ground flickers          0 / 120 resting   0 / 120 resting
```

**Zero steps held, at every shipped configuration.** The player never once stood on a chunk that
had not arrived. At r6h/3v the fall survives **20, 40, 60 and 90 m/s** with `held 0`, so the
40 m/s constant M4-PLAN guessed has at least 2.25× of margin at that view distance.

### Where it does break, because "never happened" is not a measurement

| Configuration | Held steps | Longest run |
|---|---|---|
| r6h/3v @ 40 m/s (shipped) | 0 | — |
| r6h/3v @ 90 m/s | 0 | — |
| r12h/3v @ 40 m/s | 0 | — |
| **r12h/3v @ 90 m/s** | **3 (0.42%)** | 1 step, 17 ms |
| **r16h/3v @ 120 m/s** | **12 (1.67%)** | 5 steps, 83 ms |

So the signal does fire, and it fires where the arithmetic says it should. Descending one chunk
row at r6 asks for 13×13 = **169 new chunks**, and at 40 m/s a row is crossed every 0.2 s —
which 8 workers clear with room to spare. At r16 the row is 33×33 = 1,089 chunks and 120 m/s
crosses one every 0.067 s, and *there* the streamer loses. **Terminal velocity is a streaming
parameter in a physics costume, and the costume fits: 40 m/s stands, unchanged.**

Worth noticing in the same runs: **31,421 and 47,478 dropped jobs** at r12 and r16. That is the
M2a scheduler cancelling stale work as the viewer outruns it, doing exactly the job a
`System.Threading.Channels` FIFO could not — the design decision M2's reference reading bought,
being exercised for the first time by something that genuinely moves faster than the world.

### A falling step costs 16 voxel reads, not the 252 M4a predicted

M4a measured a walking step at 41 reads and computed a four-substep fall at **252**, worst
alignment. Measured, a fall at 40 m/s is **16.0**, and the shape is exact:

```
20 m/s → 2.00 substeps,  8.0 reads
40 m/s → 4.00 substeps, 16.0 reads
60 m/s → 5.00 substeps, 20.0 reads
90 m/s → 7.93 substeps, 31.7 reads
```

Four reads per substep, every time. **The reason is `ResolveAxis`'s zero-delta early-out** — a
pure fall has exactly zero X and Z displacement, so two of the three axes return before querying
anything and only the Y layer is scanned, 2×2 voxels under the box. That early-out was added in
M4a for a completely different reason: an axis with no displacement must be left *bitwise* alone
so a resting body does not creep. It turns out to make falling an order of magnitude cheaper than
the plan's worst case. **A decision made for numerical stability paid off as a performance one**,
which is worth recording because the temptation later will be to "simplify" it away.

M4a's guess was pessimistic in the same direction as its ~400-per-step guess for walking, and by
about the same factor. **Do not build any of the query memoisation M4-PLAN named as the fix.**

### The collider is three orders of magnitude inside a frame

`step ms` mean **0.013–0.015 ms**, max **0.57 ms**, against a 16.7 ms frame. The whole
solve — gravity, substeps, three axis resolves, step-up and step-down probes — is noise. Nothing
in M4 needs optimising, and the fall profile exists now as a regression detector rather than as
an open question.

### The flicker detector produced two artefacts before it was worth anything

**This is the process finding, and it is the one worth carrying forward.** The
ground-state flicker counter is one integer, and it was wrong twice — both times reporting a
small non-zero number that would have read as a real defect.

1. **It counted landings.** The first version compared ground state between consecutive at-rest
   steps. But the landing step *is* the first at-rest step and the step before it was a fall, so
   every landing scored a flicker. The first fall profile reported `1 flicker` and it was the one
   landing in the run. Fixed by making the test asymmetric — *was at rest, and is now not
   grounded* — which a landing cannot satisfy.
2. **It counted held steps.** A body standing on an unloaded chunk is grounded with its velocity
   zeroed by the contact, so it satisfied "at rest"; the step where the chunk arrived and proved
   to be air was then a textbook was-at-rest-now-airborne. The r16/120 sweep reported **5 flickers
   over 28 resting steps**, every one of them the held-then-released pattern `StepsHeld` was
   already counting in the next column. Fixed by excluding held steps from *at rest*.

Both were caught by reading a non-zero number and refusing to believe it. After both fixes every
shipped-configuration run reports **0 flickers over 120 resting steps**, and the two broken
configurations report 0 as well.

`RestingSteps` is printed next to the flicker count precisely because of this: a fall that never
lands has nothing at rest, and `0 flickers` on its own would read as a clean bill of health for a
detector that never ran. The report says so explicitly when it happens.

### The headless frame-time trap, a fourth time, and it did not catch anyone

`frame ms` across every run above sits at mean 6.90 and max 7.48 — a main loop paced by something
that is not us — while the directly-timed `update ms` max ranges from 1.1 ms to 9.3 ms across the
same runs. The two numbers have nothing to do with each other. Every player number in this
milestone is a stopwatch around the call.

### What the fall profile does *not* measure, stated so it is not over-read

**A falling player falls through air, so the profile exercises generation and not meshing.**
Every fall run reports `0 drawn`: at altitude there is no terrain in the visible box, and the
250 m runs only meet terrain in the last seconds of each fall. So these numbers are the
streamer's *generation* throughput under vertical motion, which is the half that can starve a
falling body — but a player falling down a shaft they dug would have terrain in the box on all
sides, and that case is not covered here.

The nearest available approximation is the 250 m configuration, which lands three times in a
20 s window and is the one the shipped-constant table above uses.

### M4 is done

M4a the collider, M4b motion, M4c binding, M4d feel and measurement. **Nothing measured in M4a
or M4b was overturned**, by playing or by profiling; two guesses were beaten in the safe
direction (query counts, twice) and one constant was confirmed with headroom (terminal velocity).
The playable half was answered by a person — jump natural, uphill walks like ground, the tunnel
fit as tight as predicted, reach generous and kept at 5 m.


## 2026-08-30 — Settled: reach stays at 5 m, and two things now want game modes

**"Generous but I'll allow."** That closes the last of M4d's feel questions, and it closes it in
the right direction: over-reach costs a slightly floaty crosshair, under-reach costs not being
able to touch the thing you are looking at, so **generous is the right way to be wrong in a
sandbox**.

M4-PLAN's constants table guessed **3.5 m** and it was never shipped — the estimate has been
struck through rather than quietly deleted, because a guessed number that was overruled by play
is more useful in the record than absent. Every copy of "M4d settles it" is now resolved: the
plan's table, its M4c and M4d sections, two TODO entries, two CLAUDE.md passages and
`PlayerController.ReachMetres`' own doc comment.

**Recorded revisit trigger: a survival mode wants 3 m or 2 m.** Not a tuning note. The number is
right for *this* mode and wrong for one that does not exist, so it changes when the mode does.

### The count worth noticing: two things now wait on a game-mode concept

- **Reach** — 5 m creative, 2–3 m survival.
- **The precision mining modifier** — whether Ctrl+chip should be creative-only, which was the
  original shape of the 2026-08-30 mining proposal before it became a pure modifier.

Neither justifies building game modes on its own, and both were deliberately deferred for the
same reason: M4c made fly mode a *measurement instrument*, so there is nothing honest to gate on
today. **Two is where the shape of the thing starts being knowable**; a third would make it
overdue. Written down so the count is visible rather than rediscovered one requirement at a time.

### Where M4 stands

**M4a, M4b and M4c are done, and M4d's entire playable half is answered:** the jump feels
natural, 0.25 m ground walks like ground with no jumping needed uphill, you "sortof" fit down a
tunnel you carve exactly as the arithmetic predicted, and reach stays at 5 m. **No constant
measured in M4a or M4b was overturned by playing.**

What is left of M4d is the measurement half, untouched: fall mode in the profiler, substeps and
voxel queries per step, ground-state flicker, collider cost timed directly — and *steps spent
held*, which still needs the signal M4b deliberately did not build, because `MoveResult` cannot
tell "stopped by rock" from "stopped by an unloaded chunk". That is the number that picks
terminal velocity, and it is the last open question in the milestone.


## 2026-08-30 — Two mining shapes: the sphere keeps the click, Ctrl takes one voxel

Decided and built. `Carve.Chip` and `Carve.Voxel` join `Carve.Swing` and `Carve.Sphere`; the
bare left click still removes the tool's sphere and **Ctrl+left takes exactly the highlighted
voxel**. 9 new tests, suite **452 → 461**, six break-recipes and all six caught.

### The proposal arrived inverted, and the inversion had a price tag

The ask was single-voxel as the default with the sphere hidden under a modifier, for the person
building something precise. Both capabilities are right; **which one the bare click does turned
out to be an economy decision rather than a control one.**

`hand_pick`'s 0.4 m sphere takes roughly **17 voxels**. A single-voxel default is therefore about
**17× slower to excavate** with the starting tool — in a game whose premise is a mining
operation fighting water and gravity, and against DESIGN's own stated argument that volumetric
inventory exists to make big excavations meaningfully productive. It also reverses a DESIGN
line: *"Mining is sculpting: a pick swing removes a sphere or cone of material."*

So the modifier points the other way: **sphere on the click, precision on Ctrl.** Identical
capabilities, identical one modifier, no DESIGN reversal, and the productive action stays the
default in the game about being productive. DESIGN's line now records that the alternative was
considered and why it lost, which is what that document is for.

**A quiet dividend:** `radiusMetres` keeps meaning "what a swing takes". Flipping the default
would have made it "what the *modifier* swing takes" — a change to a live field in
`mods/minercraft/tools/*.json`, and non-negotiable 5 says the base game is a mod, so that is a
format change and not a rename.

### What Chip actually does, and the two exemptions it does not get

**It takes `hit.Voxel` itself, with no `SwingCentre`.** A sphere is centred on the hit voxel's
centre because a radius has to mean the same thing from every angle; a chip is the voxel. Which
means **`VoxelHighlight`'s outline stops being an indication of roughly where the bite lands and
becomes literally the thing about to be removed** — that is the whole reason precision reads as
precise.

**Same hardness gate and same cooldown.** Precision changes how much a swing takes, never what
it is strong enough to take, and never how fast it fires. Exempt it from hardness and the
modifier becomes a way to mine anything one voxel at a time, making M3's entire progression
advisory. Exempt it from cooldown and it becomes the *efficient* way to mine as well as the
accurate one, and the sphere stops being the default in practice while still being it in code.

### The refactor is load-bearing, and one recipe proves it

The per-voxel gate — unloaded, already air, too hard, taken — moved out of `Sphere`'s inner loop
into a private `Carve.Take` that both shapes share. Not tidiness: **removing the hardness check
from it fails 9 tests, seven of them pre-existing sphere tests.** One edit breaking both shapes
is exactly what a second copy would not do, and it is the same argument `WorldGenerator` makes
about `IsCaveAt` and `GenerateTerrain` sharing one definition of the cave field.

### Six recipes, six caught — and that is worth saying out loud

| Break | Caught by |
|---|---|
| `Chip` spheres instead of taking the hit voxel | 2 |
| Hardness gate removed from the shared `Take` | **9**, seven of them sphere tests |
| `Voxel` miscounts `Candidates` on a refusal | 2 |
| Unreachable reported as `TooHard` | 3 |
| Air identity check dropped | 6 |
| `Chip` ignores the tier threshold | 1, and only 1 |

Nothing came back green, which is unusual here and is stated rather than assumed to mean the
battery was easy: the shape has no geometry to get wrong, which is most of the explanation. The
last row is the thin one — a single test stands between precision and ignoring tiers entirely.

One documentation correction on the way: the test file first claimed recipe 3 was caught by the
`IsAccounted` assertion. It is caught by the exact-`CarveResult` comparisons, which fire first;
`IsAccounted` fails too but is not what the run reports. Fixed, because a test file that
misattributes its own coverage is how a redundant assertion gets deleted later.

### Known and unfixed: Ctrl is also FlyCam's descend

Shipped on Ctrl as asked, and it is fine in **walk** mode, where Ctrl is free. The cost is **fly**
mode: holding Ctrl descends at `MoveSpeed`, so at the default 6 m/s and `hand_pick`'s 0.3 s
cooldown a builder sinks about **1.8 m between chips**. Alt is free in walk mode and means
*crawl* in fly mode, so "slow down to do fine work" pairs rather than fights — **Alt is the
better key and the swap is one line.** Left on Ctrl pending a play-test rather than changed
unasked.

### Not built, and still M6's

Inventory. `CarveResult.Removed` is the number it will read as a volume, and it is already
correct for both shapes: a chip deposits one voxel, **15.625 litres**, and a `hand_pick` sphere
about 268. *Mining granularity* and *inventory unit* stay independent decisions — volumetric
inventory needed no adjustment for either shape.


## 2026-08-30 — Raised: single-voxel mining, and which way the modifier should point

A design decision arrived from the play-test rather than from the plan: **mining one voxel at a
time as the default, with the sphere carve kept under a modifier** — Ctrl, because Shift is
sprint — for the person building something precise. Recorded, not built. Four things it touches,
one of which is a locked decision.

### It reverses a DESIGN line, and the reversal has a price tag

DESIGN, under *Mining and placement — decoupled granularity*: **"Mining is sculpting: a pick
swing removes a sphere or cone of material."** Changing which one a bare click does is a change
to that line, and DESIGN is the document that exists so decisions are not quietly reversed.

The price is measurable rather than aesthetic. `hand_pick`'s 0.4 m sphere takes roughly **17
voxels**; a single-voxel swing takes one. So the default becomes about **17× slower to
excavate** with the starting pick — and DESIGN's stated argument for volumetric inventory is
that it "makes big excavations meaningfully productive", in a game whose premise is a mining
operation fighting water and gravity. **The Taj Mahal case and the mining-operation case want
opposite defaults**, which is the actual tension and is not resolved by picking a key.

**Recommendation: keep the sphere on the bare click and put the single voxel on the modifier.**
Identical capabilities, identical one modifier, no DESIGN reversal, and the productive action
stays the default in the game about being productive. Precision becomes opt-in for the person
who has already decided to build something — who is exactly the person willing to hold a key.

### Volumetric inventory is safe either way, and that is worth writing down

"One block at a time into inventory" reads at first like a contradiction of DESIGN's **"inventory
is volumetric — litres of stone, not '3 stone blocks'"**. It is not. A voxel is 0.25³ m³ =
**15.625 litres**. *Mining granularity* and *inventory unit* are independent: a single-voxel
swing deposits 15.6 L of stone, a sphere deposits ~268 L, and the inventory never counts blocks
in either case. Recorded so it does not get re-litigated as a conflict.

### Ctrl is already bound, in exactly the mode that wants it

`FlyCam` binds **Ctrl to descend** and **Alt to crawl**, both fly-only. So Ctrl and Alt are free
in **walk** mode and taken in **fly** mode — the exact inverse of "multiblock in creative/fly
under Ctrl". Ctrl+click while flying would sink and carve at once. Shift is sprint and stays
sprint, which was the right call and is confirmed by play. Either accept the overlap, move
fly-descend, or use a non-modifier binding alongside the tier number keys.

### Fly mode is a measurement instrument, not a creative mode

M4c deliberately made fly the thing `--profile` implies and the thing the walk toggle refuses to
leave mid-run. Gating a gameplay privilege on it would tie a game rule to a profiling flag, and
there is no creative/survival concept in the project to gate on instead. **Make it a modifier
now**; add a mode gate if and when game modes exist. That is one line later and no lines now.

### Two smaller consequences

- **`radiusMetres` changes meaning if the default flips** — from "what a swing takes" to "what
  the modifier swing takes". It is a live field in `mods/minercraft/tools/*.json` and
  non-negotiable 5 says the base game is a mod, so that is a format change rather than a rename.
- **`--mine` must keep profiling the sphere** whichever way the default goes. M3d's baseline is
  the *worst* case; a profile that silently switched to single-voxel would report a cheaper
  number under the same label and read as an improvement.

Inventory itself is M6 — "SQLite save/load, brush placement, volumetric inventory" — and is
genuinely later. The mining-mode split is small and could land any time: `Carve` already gates
per voxel, so it is a mode on `MiningController` and a modifier read in `Main`.


## 2026-08-30 — Played: 0.25 m ground walks like ground

**"Walking uphill occurred naturally, I did not need to jump."** That is the question M4d exists
to ask, and the one no profile could ever have answered.

DESIGN argued that step-up is not a convenience in a quarter-metre world the way it is in a
one-metre-block game — terrain from a heightmap steps by whole voxels, which at 0.25 m is seven
steps per metre of slope, and without step-up every one of them is a wall you have to jump. That
was an argument. It is now a verdict. **Step height stays 0.5 m**, and the "one constant serves
both directions" decision keeps its single knob.

The jump was judged natural in the same session, so **M4b's constants stand as measured**:
7.5 m/s against 25 m/s², apex 1.1875 m.

### What the play-test did *not* touch, and why that is silence rather than evidence

Two recorded feel costs are still unjudged, and **ordinary uphill walking cannot reach either**:

- **Step-up guard 3's wall-hug.** It needs walking *diagonally* into a corner where one axis is a
  climbable lip and the other a tall wall; the lifted re-attempt is still blocked on the wall, so
  the climb is refused and the player stops dead. Walking up a hill will not produce it. If it
  ever reads as stuck the relaxation is still one line — require the re-attempt to be blocked on
  no axis the original move was not.
- **Step-down's 0.5 m instant drop.** That needs a lip walked *off*, not up. The trigger was
  always "does the drop jolt where the matching rise does not".

Not reported is not the same as not there, and both of these are the kind of thing a person
walks past for an hour and then hits once.

### You "sortof" fit down a tunnel you carve

The hedge is worth more than a yes would have been, because it is **precisely what M4-PLAN's
arithmetic predicts**. `hand_pick` has a 0.4 m radius; a swing centred on a voxel centre takes
every voxel whose centre is within it, which along any axis through the centre is three voxels,
a 0.75 m free bore. A 0.5 m box passes with **0.125 m either side — half a voxel**. And a tunnel
is not one swing: consecutive swings centre on different voxel centres, so the bore's axis
wanders by more than nothing. It fits on paper and it scrapes in practice.

**The cheap lever is the tool, not the player.** `hand_pick`'s `radiusMetres` from 0.4 to 0.5
makes the bore five voxels, 1.25 m, clearing 0.375 m either side — **one number in one JSON file,
no rebuild**, which is exactly what M3c made tool tiers content for. Widening the *player* is the
move M4-PLAN warns against: the player's width and the weakest tool's bite are coupled and the
width is the half that is not free, because it is also the collider, the eye offset, and every
number M4a measured. Unchanged pending a decision — a tunnel that scrapes may well be the
intended feel for the starting pick.

### What is left of M4d

The playable half is nearly done: **jump ✅, uphill walking ✅, tunnel fit ✅ (hedged)**. Reach at
5 m is the last one needing a person. The measurement half — fall mode in the profiler, steps
spent held, ground-state flicker, collider cost per step — is entirely unbuilt, and *steps spent
held* still needs the signal M4b deliberately did not add.


## 2026-08-30 — Played: the jump feels good

M4b's constants get their first verdict from a person rather than a number, and it lands on the
one the plan was least sure of. **7.5 m/s against 25 m/s² stands.** The 1.1875 m apex — the
measured one, not the analytic 1.125 m — is a keeper.

Two things may follow: a jump-height option, and optional double jump. Neither is built, and
both are recorded now so they are not re-derived later.

**A "jump height" setting is not `JumpSpeedMetresPerSecond` wearing a different name.** Turning
a height in metres back into a launch speed needs the *discrete* apex, `v²/2g + v·dt/2`, not
the continuous `v²/2g` — a jump replaces its step's gravity rather than being reduced by it,
so the rise overshoots by half a step of launch speed. Invert the continuous form instead and
the setting reads 6 cm short at 60 Hz, and **12 cm short at 30**, because the overshoot term is
rate-dependent. A height offered to a player is only honest at a stated tick rate.

**Double jump is additive and costs one counter on `PlayerState`.** It is the same category M4b
already named for coyote time and jump buffering, and `PlayerState`'s "three fields and no more,
which is what makes `Step` a pure function" comment anticipates precisely this — a fourth field
does not change that. It also does not disturb step-down's non-upward guard, which already
refuses to fire while the body is rising; removing that guard is the break-recipe that fails six
tests and stops jumping working at all.

Still unplayed and still M4d's: whether 0.25 m ground walks like ground or like a staircase,
whether 5 m of reach is too much now something owns it, and whether the box fits down a tunnel
dug with the starting pick.


## 2026-08-29 — M4c: binding, and the two recipes that came back green

The player is bound. `MinerCraft.Core/Physics/PlayerSpawn.cs` holds the three world rules —
where a spawn lands, whether the world is ready to receive a body, and what to do about a body
inside rock — and `MinerCraft.Game/Player/PlayerController.cs` holds the session: which of the
two bodies is driving, and the gate the body waits behind. 17 new tests; the suite is
**435 → 452**.

Headless from a standing start, walk mode, the default r4h/2v:

```
M4c: walk mode, body 0.50 x 1.80 m, eye 1.65 m, spawn feet 1.25 m
M4c: spawned at <15, 1.2501, 15> after 14 step(s) held for the world   (13 on a re-run)
1521 viewed (1521 tracked), 165 drawn, 0 in flight, 0 queued
1926 jobs done / 0 dropped / 0 faulted
```

**1521 / 165 / 1926 are the recorded M2d numbers, unchanged to the digit.** That is the whole
of M4c's contract with the previous two milestones: adding a player did not move streaming.
`--profile --radius=6 --vradius=3 --speed=8` still flies, still reports, and lands at 0.64% of
one core against M3d's 0.80% at the same configuration.

### The flycam was not deleted, and the toggle is one flag

`FlyCam` gains `FlightEnabled`. False in walk mode, and **only the movement is switched off —
mouse-look stays live in both modes**, because looking around is the same act whichever body
you are and because walking needs a yaw to walk along. No reparenting, no physics body node,
no second camera. `--profile` and a non-zero `--speed` both force fly mode at startup and both
refuse the F key mid-run, because handing a scripted flythrough's camera to a body would
invalidate the measurement rather than produce an interesting one.

`FlyCam.WalkForward` normalises **after** flattening, not before. Dropping the Y component of a
pitched forward vector shortens it — looking down 60° leaves 0.5 units — and `MoveIntent`
clamps intent to unit length without ever scaling it up, so the naive version walks at half
speed for looking at your feet.

### 13–14 steps held for the world, and that number is the reason the gate exists

At frame zero nothing is loaded, every unloaded voxel is solid, and a body dropped in is sealed
in rock in every direction. `PlayerController` holds it inert — not stepped at all — until
`PlayerSpawn.IsWorldReadyFor` says the chunks are there. **13 or 14 physics steps, about
0.22 s** — it varies run to run with worker scheduling, so it is a scale rather than a
constant.

**The order is gate, then unstick, then step, and it is not interchangeable.** Unsticking
against an unloaded world finds solid at every candidate height, runs to its 4 m lift bound,
fails, and reports a body stuck in open sky — which reads as the spawn being broken rather than
as the world not having arrived. `UnstickFailsAgainstAnUnloadedWorld` pins it.

The gate checks every chunk the box overlaps **and the chunk beneath each of them**. The box's
own because an unloaded one is solid and would pin the body; the layer below because that is
the floor, and standing on an unloaded chunk is standing on something that will not be there —
the body holds, then drops the moment the chunk arrives and turns out to be air.

A switch into walk mode re-enters through that same gate rather than placing the body directly.
Toggling into a wall and spawning into a cave mouth are the same event, and a second copy of
the decision is a second copy to drift.

### Six break-recipes, two green, and they are two different things

| Break | Caught by |
|---|---|
| The readiness gate always says yes | 3 |
| The gate drops the chunk beneath the feet | 1, and nothing else |
| Unstick accepts the first candidate blindly | 5 |
| `FeetOnSurface` uses the surface row, not the row above | 3 |
| **Unstick starts at the box's own row instead of one above** | **nothing — a real hole** |
| **Unstick translates instead of rebuilding the box** | **nothing — a documentation correction** |

**The hole.** Every buried-body test placed the feet on a voxel plane already, so the extra
downward candidate was the box itself and overlapped by construction. Off a plane, with a body
whose *head* is what is stuck, the lower candidate can be clear — and the rule silently becomes
"move to the nearest free plane" instead of "lift". `UnstickNeverMovesABodyDownward` was
written for it, watched fail on it, and is the only test that catches it.

That test also records a decision rather than a consequence: **a body with its head in a
ceiling is pushed up through the ceiling and onto the roof, not down out of it.** Up is the
only direction whose answer does not depend on which face happened to be overlapping, and a
body that can be pushed down by material appearing above it can be pushed into the floor.
**Recorded for M6**, which is the first system that puts a block at the player's head
deliberately.

**The correction.** Rebuilding the lifted box from the untouched `Min.X`, `Min.Z` and `Size`
rather than calling `Aabb.Translated` with a computed delta is M4a's corner-and-size rule, and
after trying to construct a failing case there is none: `Min.Y + (y − Min.Y)` is exact for every
pair a 4 m lift can produce inside the few-kilometre world M4a's float analysis allows, and the
horizontal `+ 0.0f` is exact for every float except `−0.0f`, which `Aabb.FromFeet` cannot
produce. Kept because it makes the lift obviously vertical, **not** because a test can tell.
Same shape as M4a's "`ContactEpsilonMetres` can be zeroed with the suite green".

### Feet exactly on a voxel plane is a state that survives exactly one step

The end-to-end spawn test was written expecting the first step to be bitwise identity and it
was wrong: the step lifts the body by exactly `ContactEpsilonMetres`. Gravity pushes it a
fraction into the floor and the −Y resolve places it the epsilon clear — 1.25 m becomes
1.2501 m, which is the number the headless run above prints.

So **feet exactly on a plane exists only at the spawn and at a mode toggle**, and one step of
anything at all is a tenth of a millimetre above it. That is why M4b's
`ALiftOfExactlyTheStepHeightClearsALipOfExactlyTheStepHeight` constructs the position by hand
instead of walking a body into it, and it is the thing to check before writing any other
bitwise assertion about a resting body. The second step is bitwise identical to the first, so
the resting position is stable — which is what M4a's corner-and-size storage exists to give.

### Reach moved, and deliberately did not change

`ReachMetres` is `PlayerController`'s now, closing M3d's recorded item. `MiningController`'s is
`required` and defaultless, so there is no second number to drift from the first —
`PlayerController.DefaultReachMetres` is the only literal.

**Still 5 m.** M4-PLAN's constants table says 3.5 m and the play-test note says 5 m reads a
little long at twenty voxels; both are opinions about feel and **M4d settles them by playing**.
Moving the field and retuning the number in the same pass would make it impossible to tell
which change did what.

### The step runs in `_PhysicsProcess`, and the order M4-PLAN asked for holds

Player → viewer → pipeline. Godot runs every physics tick of a frame before that frame's
`_Process`, so the body is solved against the freshest world the streamer has managed, and the
viewer that decides what loads next is placed from where the body actually ended up.

The camera and the mining ray share one origin without deriving it twice: `_PhysicsProcess`
assigns the camera from `PlayerState.EyePosition`, and `UpdateMining` reads the camera. In fly
mode the camera is the only truth and nothing changes.

### Still open, and it is the half that has to be played

M4c is verified headless — it spawns, it holds, it does not perturb streaming. **Nothing here
has confirmed that walking feels like anything**, because headless has no keyboard. Whether
0.25 m ground walks like ground or like a staircase, whether the jump is right, whether 5 m of
reach is too much, and whether the box fits down a `hand_pick` tunnel are all M4d's, and all
need a windowed run with a person at it.

## 2026-08-29 — M4b: motion, and the snap that keeps M4a's epsilon claim true

`MinerCraft.Core/Physics/` gains `PlayerMotion` and `PlayerMotionConfig`. Gravity, jump,
terminal velocity, step-up and step-down, sitting on M4a's collider and adding only state.
35 new tests; the suite is **400 → 435**.

`PlayerMotion.Step(world, materials, state, intent, config, dt)` is a pure function of a state,
an intent and a world — no clock, no accumulator, no field anywhere in the file. The step's
length arrives as an argument, which is what makes a 10,000-step fall a millisecond-scale test
and what will let M4d drive the body headlessly.

### Ten break-recipes, two holes, both closed

The rule is three for three and now four: a green run is not evidence. Ten recipes were applied
to the finished code and the suite re-run each time.

| Break | Caught by |
|---|---|
| Step-up ignores the grounded guard | `StepUpRefusesWhenTheBodyWasNotOnTheGround` |
| Step-up ignores the non-upward guard | `StepUpRefusesWhileTheBodyIsMovingUpward` |
| Step-up accepts a partial horizontal re-attempt | 3, including both refusal tests |
| Step-down ignores the non-upward guard | **6**, every jump test among them |
| Step-down ignores the was-grounded guard | the cliff half of the step-down test, and the long fall |
| Contact does not zero velocity | **6**, standing still and every landing |
| The jump is reduced by its own step's gravity | the apex test, one-sided as designed |
| The walk direction is normalised unconditionally | the half-pushed-stick row of the speed theory |
| **Lift accumulated instead of snapped to its target** | **nothing** |
| **Step-up settles further than it lifted** | **nothing** |

The step-down guard being caught by six tests is not redundancy, it is the shape of the bug:
without it, the step-down probe finds the floor the jump just left and snaps the body back onto
it, so **jumping does nothing at all**. Every test that involves leaving the ground fails.

### The hole worth reading twice: a claim in *another milestone* was about to go stale

`TryStepUp` places an unobstructed lift at its exact target rather than at whatever three
substeps of a third of the step height accumulate to. Nothing caught removing that, so the
obvious justification — "accumulation error refuses a legal step" — is **wrong on its own**: an
ordinary resting body floats `ContactEpsilonMetres` above the floor, and 0.1 mm swamps a few
ULP. With the epsilon in place, snapping changes no outcome and the suite passes either way.

Running the two together is what found the real answer:

| | lift snapped | lift accumulated |
|---|---|---|
| epsilon 1e-4 (shipped) | green | green |
| epsilon 0 | green | **4 tests fail** |

**M4a recorded that `ContactEpsilonMetres` can be set to zero with the whole suite green** — the
evidence for the half-open interval being the load-bearing belt rather than the epsilon. That
claim was still true after M4b landed *only because of the snap*. Without it, a documented
finding in the previous milestone would have quietly stopped holding, with nothing failing to
say so.

`ALiftOfExactlyTheStepHeightClearsALipOfExactlyTheStepHeight` now catches it without touching
the epsilon, by placing the feet **exactly** on a voxel plane. That is not a contrivance
invented for the test: M4c's spawn puts the body at a surface height and a fly-mode toggle puts
it wherever the camera was, and neither goes through a resolve that would add the 0.1 mm.

The second hole is smaller and its test needed constructing rather than finding. A step-up
settles by exactly what the lift achieved, so it is a climb and never a fall — what that
protects is **step-down's distance limit**, which deliberately refuses to snap the body down
more than the step height so that walking off a real ledge falls. A settle that could go
further gives the same rule two answers depending on whether the body happened to climb
something first. Reaching it needs a body that clears what it just climbed in a single step,
which walking cannot do — 0.075 m against a body 0.5 m wide — so the test says so and uses a
45 m/s body, the same condition M4a's remarks name for a mine cart.

### The jump apex is 1.1875 m, not the 1.125 m the plan derived

M4-PLAN's table gives 7.5 m/s against 25 m/s² as "→ 1.125 m apex, clearing a 1 m (4-voxel)
ledge with 0.125 m spare". That is the analytic `v²/2g`, and the discrete integration does not
land on it. Measured to four decimals: **1.1875 m**.

The difference is not error and it has a sign. A jump *replaces* its step's gravity rather than
being reduced by it — the body leaves at exactly the launch speed — so the first step travels
`v·dt` where the continuous solution travels the average over that step, and the rise overshoots
by exactly half a step of launch speed, `v·dt/2` = 0.0625 m. So the real clearance over a
four-voxel ledge is **0.1875 m, half again as much as the plan claimed**, and it is
**rate-dependent**: the same constants at 30 Hz overshoot twice as far. Nothing depends on that
today because the player runs at one rate, but a jump height that changes with the tick rate is
the sort of thing that gets diagnosed as a physics bug.

The apex test asserts one-sided — at least the analytic value, at most one step of launch speed
above it — because the *direction* is the design claim. Reversing the jump/gravity order fails
it, which was recipe seven.

### A recorded cost, and one thing M4d needs that does not exist yet

- **Step-up's "the re-attempt must fully succeed" guard has a feel cost.** Walking diagonally
  into a corner where one axis is a climbable lip and the other is a tall wall: the lifted
  re-attempt clears the lip, is still blocked by the wall, and the whole step-up is refused. A
  player hugging a wall while walking up a slope stops dead until they let go of the wall
  direction. That is M4-PLAN's specified behaviour and it is safe; **if M4d finds it reads as
  stuck, the relaxation is one line** — require the re-attempt to be blocked on no axis the
  original move was not already blocked on. Pinned by a test either way.
- **`MoveResult` cannot distinguish "stopped by rock" from "stopped by an unloaded chunk",** and
  that is exactly M4d's *steps spent held* — the number that is supposed to pick terminal
  velocity. It is a flag on `IsSolidUncounted` and a field on `MoveResult`; deliberately not
  built now, because an unread flag with no profiler consuming it is a number waiting to be
  believed.

### Cost, and the decisions that are M4d's to overturn

A walking step is **one `Move`** and M4a's ~41 voxel reads; a successful step-up is **four** —
the failed original attempt, the lift, the re-attempt, the settle — so roughly 4× an ordinary
step and nowhere near anything else in a frame. Both are asserted loosely, as regression
detectors rather than measurements to defend to the digit.

Three shape decisions, all of them cheap for M4d to reverse:

- **Horizontal velocity is replaced from intent every step rather than accelerated.** Stop
  pressing forward and the body stops that frame; no friction, no momentum, full air control.
  That is the snappiest model and the one whose faults are easiest to judge by playing, which is
  the point — acceleration, air control and ground friction are three separate feel decisions
  and a person makes them. The vertical component *is* integrated.
- **One `StepHeightMetres` serves both step-up and step-down.** M4-PLAN specifies step-down as
  triggering "within the step height", and stepping down a lip you just stepped up should not
  behave differently. **Revisit trigger:** M4d, if a 0.5 m instant drop reads as a jolt where
  the matching rise does not.
- **Jump is level-triggered**, so holding the control bounces. Edge-triggering needs the previous
  frame's button state in `PlayerState`, which is jump buffering's field and therefore M4d's.
  Coyote time, buffering and variable height are all additive — none of them changes this model's
  shape.

**Reach did not move.** M4-PLAN's constants table lists it under M4b; it is not a body dimension,
it is what an arm does, and it belongs to the controller M4c builds. `EyeHeightMetres` *is* here,
because it is the third dimension of the same body whose width and height are here and splitting
those across two files is how one of them goes missing.

`TestWorld` moved out of `VoxelColliderTests` into a shared `PhysicsTestHelpers`, for the reason
`ShippedContentTests` exists: the property these doubles preserve is that an unloaded chunk
answers *air **and** false*, and a second copy that drifted would let a body which ignores the
return value pass its own suite.

## 2026-08-29 — M4a: the collider, and the four ways it was broken on purpose

`MinerCraft.Core/Physics/` holds `Aabb` and `VoxelCollider.Move(world, materials, box,
displacement)`. Axis-separated resolve, substepped, with an unloaded voxel treated as solid.
33 new tests; the suite is **367 → 400**.

It is a pure function of a box, a displacement and a world — no gravity, no input, no state
between calls — which is why M4a came first and why every one of its tests runs headless in
milliseconds.

### Watched to fail, and one of the four was not caught

The rule M3 earned three times is that a green run is not evidence. So four break-recipes were
applied to the finished code and the suite re-run each time:

| Break | Caught by |
|---|---|
| `MaxSubstepMetres` 0.2 → 1000 | **14 of 33**, the sealed-room fuzz among them |
| Unloaded voxel reads as air | the unloaded-chunk test, alone |
| `LastIndex` uses `floor` instead of `ceil − 1` | the half-open geometry test and the shaft test |
| Cancel every axis when any one is blocked | the corner-snag test, as designed |
| **Layer taken from "one past where the box was"** | **nothing** |

One row of that table is worth reading twice. The closed-interval break is caught by three
tests, and every one of them is a situation where a face lands exactly on a voxel plane: the
direct geometry test, the exact-width shaft, and the box that starts inside a wall. **Not one
of the ordinary walking-and-falling tests notices** — the contact epsilon stops every resolved
face a tenth of a millimetre off the plane, so in ordinary motion a face is never exactly on
one and the two forms agree. *The epsilon masks the rule it is a second belt for.* The
geometry test in particular reads as a restatement of the type's doc comment and is not; it
now says so, so it does not get deleted for looking redundant.

(That count was written as "two" first, from an earlier run, and was already stale by the time
it was typed — the third catching test had been added in between. Re-running the recipe caught
it. The rule about verifying a claim in a comment applies to comments about *coverage* too.)

The last one was a hole, and it was a hole in exactly the shape M3c warned about: a paragraph
of doc comment claiming a design choice was load-bearing, with no test that could tell the two
choices apart.

**`ResolveAxis` takes its voxel layer from the *moved* box, never from "the layer one past
where the box already was".** While the box is outside the wall those are the same layer, so
nothing in the suite could distinguish them. Once the box is *inside* one — a mode toggle in a
wall, M6's placement, the knife edge below — the second form finds no new layer ahead and waves
the move straight through, walking the box out the far side in about ten steps. A test that
deliberately violates the precondition (start overlapping, push further in, assert it never
gets deeper) now catches it in one run.

A fifth recipe was run and **passed**, which is the useful half of this exercise.

### The epsilon is not the thing that makes resting contact work

M4-PLAN specified both belts: *resolve to the contact face minus an epsilon*, **and** *treat
exact touching as not overlapping*. Setting `ContactEpsilonMetres` to zero leaves the whole
suite green, so the second belt is the load-bearing one. A face lying exactly on a voxel plane
is touching rather than inside, voxel planes are exact in binary because 0.25 m is a power of
two, so the snap is exact and needs no margin at all.

The epsilon is kept — it is what covers a voxel size that is not a power of two, or coordinates
too large for a plane to be exactly representable — but it is now documented as belt and braces
with a measurement behind the claim rather than as the mechanism.

**It has an honest cost, which is why this was worth checking.** A gap exactly the box's width
has nowhere for that margin to come from, so a 0.5 m box pushed sideways in a 0.5 m corridor
ends 0.1 mm inside the far wall. It cannot pass through either side, and a straight drop down
such a shaft is exact — an axis with no displacement is never resolved at all — and both are
pinned by tests. Zero epsilon would remove the knife edge and remove the defence with it.

### `Aabb` stores a corner and a size, and that is what makes standing still stable

The obvious representation is two corners. It creeps. A resolve places one face from the
blocking voxel's index and derives the other from the size, so if the size is itself recomputed
as `Max − Min` every step it rounds differently each time and a resting player drifts by a ULP
per frame forever.

Stored as `Min` + `Size`, a resolve is a pure function of the blocking voxel's index and the
constants — literally `(k + 1) × VoxelSize + epsilon` — so the same contact produces bitwise
the same box every step. 10,000 steps of gravity into a floor: one settle of 0.1 mm on the
first step, then not one bit changes, and `grounded` never flickers.

The other half of that is `ResolveAxis` returning early on a zero delta rather than translating
by zero. Adding `0.0f` is exact for every float except `-0.0f`, and "except" is how a drift test
starts passing for the wrong reason.

### The query-cost guess was pessimistic, and M4d does not need to check it

M4-PLAN guessed ~400 `TryGetMaterial` calls per step, from 32 voxels of box × 3 axes × 4
substeps, and named memoising the chunk across a scan as the fix if it mattered.

**Measured: a walking step costs 41.** A resolve reads one voxel *layer* — the one the leading
face ends in — rather than the whole box, and walking is one substep. The worst alignment is 63
per substep, so 252 at a four-substep terminal-velocity fall. Nothing needs memoising and
nothing needs a spatial structure.

One detail from the measurement that the arithmetic in the plan missed, and that will bite
again: the layers are not the 16 + 4 + 16 the box's nominal 2 × 8 × 2 suggests. **A 0.5 m box
that is not grid-aligned straddles three voxel columns, not two.** The first horizontal resolve
puts it off-grid, so the second one costs half as much again.

### Recorded, not solved

- **Float positions cap the usable world at a few kilometres**, long before `VoxelPos`'s `int`
  does — one ULP passes the contact epsilon around ±800 m. **Revisit trigger:** a world meant
  to be walked further wants a *floating origin*, which is what every large-world engine ends
  up building, not a wider float that `VoxelPos` would outrun anyway.
- **A displacement needing more than 1,024 substeps is refused.** 204 m in one step is a
  velocity that has blown up, not a fast body, and the alternative is a loop long enough to
  look like a freeze. Same class of bound as `ToolDefinition.MaxRadiusMetres`, and an estimate
  labelled as one.
- **`VoxelCollider.OverlapsSolid` is public** because M4c's unstick rule needs exactly it and
  so does every motion test's postcondition. `ResolveAxis` already gives *partial* unstick — it
  clears the layer the leading face is in, which is what the fifth break-recipe now tests — but
  not the full rule, which stays M4c's.

## 2026-08-29 — M4 planned: the milestone where the world starts deciding where the player is

No code. [docs/M4-PLAN.md](docs/M4-PLAN.md) splits the player into M4a–M4d the way M1 and M3
were split before anything was built, and the planning pass turned up more than expected.

### The structural thing, which is not the collision

Every milestone so far has taken the viewer's position as an **input**. The flycam is told
where to go by the keyboard, the streamer follows, and the world is whatever finished loading
by the time the camera arrived. M4 inverts it: the player's position becomes an **output of a
query against the very chunks the player decides to load**. That is a cycle, and how it is
broken is the decision the milestone turns on.

It is broken twice. The player is solved at step *N* against the world as loaded at step
*N*−1; and an unloaded voxel is **solid**, so a cycle that cannot close leaves the player
stopped at the edge of the loaded world rather than outside it.

`ChunkStreamer.TryGetMaterial` sets `materialId` to air *and* returns false, so a caller who
ignores the return value gets exactly the wrong answer. This is M3b's `Blocked`-is-not-`Missed`
at a layer where the consequence does not merely confuse: the player falls out of the world
and accelerates away from every chunk that could have stopped them.

### Walking never tunnels and falling always does

The number that made this concrete: move-then-resolve is exact below **15 m/s**, because the
thinnest obstacle is one voxel (0.25 m) and a step is 1/60 s. A sprint is under that; a fall
passes it in well under a second.

That is the worst possible shape for a bug — it cannot appear while you are testing movement,
only later, once gravity has had two seconds to work. The fix is substepping at 0.2 m, capped
on **displacement rather than the box's dimensions**, since a 0.25 m floor is thinner than the
player. Four substeps at terminal velocity, one whenever walking. A swept AABB is the exact
alternative and is rejected with a named trigger — something moving the player at 100 m/s —
because sweeping is precisely what DESIGN rejected for snagging on flat walls.

### Two couplings that were not visible before writing it down

**Terminal velocity is a streaming parameter wearing a physics costume.** There is 16–24 m of
loaded world beneath the player, which 40 m/s crosses in 0.4–0.6 s. How fast the player may
fall is a statement about how fast chunks arrive underneath them, so M4d measures the streamer
and the measurement picks the constant.

**The player's width and the weakest tool's bite are coupled, and neither can move alone.**
`hand_pick`'s 0.4 m radius removes every voxel whose centre is within 0.4 m — three voxels
along any axis through the centre, a 0.75 m free bore. A 0.5 m box passes with 0.125 m either
side. Widen the player past 0.75 m and the first thing a new player does — dig into a hillside
with the starting pick — produces a tunnel they cannot walk down.

### The trap is in the binding, not the physics

M4a and M4b are deterministic, headless and fuzzable. M4c is where a player controller meets a
streaming system, a profiler and a scripted flythrough that were all built assuming the camera
goes where it is put.

`FlyCam`'s own doc comment says it is "replaced by a real player controller with AABB
collision at M4". **That comment is wrong and following it would destroy the measurement
apparatus M2d and M3d exist to provide** — `--profile` assigns `GlobalPosition` directly,
`AdvanceFlythrough` snaps the camera every frame, and neither works on a camera parented to a
physics body. So M4 adds a walk/fly toggle with `--profile` implying fly, which also keeps
M4d's numbers comparable against M3d's.

### The fuzz oracle, found before the code exists this time

Seal the player inside a closed shell of solid voxels and apply thousands of random
displacements, including magnitudes far past terminal velocity. Escaping a sealed room is a
**boolean** — no reference implementation, no sampling, no tolerance, which is what M3b's
first oracle failed to be. Removing the substep cap must break it almost immediately; if it
does not, the displacements are too small.

The working rule that is four for four — watch a fuzz test fail before trusting it — is now
being applied at planning time rather than after a green run.

### Smaller decisions worth keeping

- **`grounded` is an output of the solve**, not a probe: "the −Y resolve fired this step".
  Gravity applies every step, so a standing player always resolves against the floor. It
  cannot disagree with the collision that just happened, and it costs nothing.
- **60 Hz for the player, not the 20 Hz sim tick** DESIGN specifies for machines. 20 Hz is
  50 ms of input latency. Machines are simulated; the player is driven.
- **Solidity is a predicate — `id != air && !IsFluid(id)`.** `MaterialTable.IsFluid` is already
  projected, so this is correct today and still correct at M8, and unlike `VoxelRay` it needs
  no recorded revisit trigger.
- **Resting contact, not tunnelling, is what kills hand-rolled colliders** in practice: a
  millimetre of jitter with `grounded` flickering and jump misfiring. Its test is standing
  still for 10,000 steps, which no fuzz would have found.
- **Step-up is load-bearing at 0.25 m**, not a convenience. Heightmap terrain steps seven times
  per metre of slope, so without it every slope is a wall. Step-down is the half people forget,
  and its absence is a camera that bobs continuously downhill.
- **Recorded trigger:** material appearing *inside* the player's box has no answer at M4. M6's
  placement can do it deliberately and M9's collapse by accident; M4c's unstick rule is the
  seed of the policy.
- Walking into your own tunnel is walking into the dark until M5. Written down because it will
  otherwise be reported as a bug.

### One thing acted on rather than planned

**`FlyCam`'s own doc comment has been corrected**, because leaving it would have been leaving a
loaded instruction next to the code. It said the class was "replaced by a real player controller
with AABB collision at M4" — a fair guess when it was written at M0, and by now the opposite of
what M4 should do. A stale claim in a `.md` gets re-litigated; a stale comment sitting beside the
code gets *followed*. The new comment says what supersedes it and why, and points at M4c.

## 2026-08-29 — M3 played, and the first milestone judged by feel passes

Matthew ran it. **The highlight draws correctly and mining feels good.** That closes M3
for real: it is the first milestone whose output was a feeling rather than a number, and
the number half — a pick swing costing a fifth of a percent of one core — was never going
to settle it on its own.

### Two outcomes worth keeping

**The highlight is verified.** It was the one M3d deliverable a headless run could not
check, since there is no renderer to draw into; all the profile proved was that it
constructs without throwing. It draws where it should, which also means the wireframe box
is inflated by the right amount — coplanar lines would have z-fought into a moving dashed
mess and been reported as a rendering bug.

**Reach reads a little long at 5 m.** Called fine for a creative-mode flycam, and it is,
so the number is unchanged. It is recorded rather than tuned because **reach is the
player's, not the tool's**, and M4 is where a player exists to own it — that is the same
argument that kept it out of the tool JSON in the first place. A survival reach wants to
be shorter; at 0.25 m voxels, 5 m is twenty of them.

### What that leaves open

Three windowed questions, down from four, and none of them M3's: whether the free budget
removes the stutter, and M1d's `RenderingServer` versus `MeshInstance3D` and 2×2×2
mesh-block questions, which want the draw-call counter `StreamingProfiler.RenderLine`
already prints.

Instant versus progressive break damage stays open and is now answerable by playing rather
than by measuring, which is the state it was meant to reach.

## 2026-08-29 — M3d: mining is bound, and a pick swing fits in a frame with room to spare

367 tests passing, up from 364. **M3 is done.** You can hold the left mouse button and
dig.

### Built

`MiningController` casts every frame, outlines what it finds, and swings when the button
is down and the tier has cooled down. `VoxelHighlight` draws a wireframe box around the
target through the `RenderingServer`, one instance moved rather than churned.
`Carve.SwingCentre` and a `Carve.Swing(hit, tool)` overload put the centring decision in
Core. Number keys pick a tier; `--mine` holds the button for a whole profile window.

### The answer to the question M3 was really asking

**Yes, comfortably.** Mining continuously — five swings a second, the tool's own cooldown
— while flying at 8 m/s with r6h/3v:

```
                     streaming only     + continuous mining
update ms  mean           0.055               0.070
           p99            0.910               1.038
           max            2.036               2.118
main thread               0.80%               1.02%  of one core
```

A frame at 60 Hz is 16.7 ms. Mining adds about a fifth of a percent of one core and moves
the p99 by a tenth of a millisecond. M3-PLAN's ordered list of answers for if it did not
fit — mesh only the dirty region, then smaller mesh blocks, then accept a frame of latency
— is not needed, and should stay unbuilt until something asks for it.

**The swing itself was never going to be the cost.** Raycast plus carve is 0.044 ms mean
and 0.17 ms max on the sim thread. Everything expensive is the remesh, which runs on a
worker, and the only part of that on the main thread is turning the result into an
`ArrayMesh` — which is `update ms`, already budgeted since M2d.

### M3a's clone estimate: right per chunk, seven times pessimistic per swing

M3-PLAN guessed "roughly one 2 KB clone per chunk per edit-and-remesh cycle" and, from
that, "a swing touching eight chunks is about 16 KB of gen0 garbage". Measured over 111
swings: **1.2 clones and 2.4 KiB per swing**, and 1.3 chunk remeshes per swing.

The per-chunk figure was right. The per-swing one was wrong because eight chunks is the
*worst* case — a swing landing exactly on the corner where eight meet — and not the
typical one. A 0.5 m sphere centred on a voxel centre is four voxels across in a 32-voxel
chunk, so it usually stays inside one. The estimate deserved its label; this is what
settling one looks like.

### Centred on the voxel's centre, and that belongs in Core

The open question from M3c, decided. The alternative was the point where the ray entered
the voxel, and it loses on two counts: that point lies exactly on a face, so half the
sphere is in air before the swing starts, and how much of it is depends on the angle of
incidence — so a radius written in a mod's JSON would mean something different on every
swing. The voxel centre is grid-aligned, identical from every angle, and identical between
two swings at the same target.

The cost is real and small: aim near the edge of a voxel and the hole is centred up to an
eighth of a metre from the crosshair. With a highlight box drawn around the target, that
is a discrepancy the player can see the reason for — which is most of why the highlight is
not polish.

`Carve.SwingCentre` rather than one line in a Godot file, because where a swing lands is a
game rule and a multiplayer server would need it. Measured, for the record: 11 of 33
candidates per swing were already air, mining downward into a surface. Entry-point
centring would push that toward half.

**Watching the test fail taught something about the test.** Two swings at the same voxel
from different entry points agreeing is a *weak* claim: `VoxelHit` does not carry the ray,
so `Carve.Swing(hit, ...)` cannot centre on the entry point — the type makes the wrong
answer inexpressible, and any voxel-derived centre passes. Moving the centre to the
voxel's min corner left it green. The assertion with teeth is that the removed set is
**symmetric about the hit voxel**, which separates the centre from the corner. Four for
four now.

### The headless frame-time trap, for the third time

The first mining run reported `frame ms` max 19.555 against the control's 6.944, which
looks like a stutter caused by mining. It is not: `update ms` max moved 2.036 → 2.165 in
the same pair, a tenth of a millisecond, and two repeat runs came back at 7.073 and 6.944
with no spike at all. **Frame time in a headless run is the wrong instrument**, recorded
twice before and now walked into a third time. `update ms` times our own work directly;
use it.

### Smaller things

- **The tier list comes out of the registry**, so the number keys are load order and a mod
  that adds a tier appears at the end. Nothing in the Godot layer knows a tool by name.
- **The strongest tier is selected at startup**, not the weakest. A truer tutorial would
  start on `hand_pick`, but the first swing at stone would then do nothing and read as a
  broken build. Press 1 to feel the gate; the tier and its numbers print when you switch.
- **Reach is the player's, not the tool's** — 5 m, on `MiningController`, and it belongs
  to M4's controller when there is one. A tool tier says how big a bite it takes and what
  it is hard enough to take; how far an arm reaches is not that.
- **Esc stops mining as well as looking.** Without gating on the flycam's capture state,
  clicking on something else while the window has focus digs a hole.
- **`--mine` aims straight down** from a camera four metres above the surface. Aiming
  along the flight path would spend the run pointing at sky, and a measurement of mining
  that mostly misses is not one.
- **The highlight is not visually verified.** Headless has no renderer, so all that is
  proven is that it constructs and does not throw. It wants a windowed run, alongside the
  three render questions already waiting for one.

## 2026-08-29 — M3c: the carve, and tools that arrive from JSON

364 tests passing, up from 307. A swing now takes a sphere of material out of the world,
and what it will and will not take comes from a mod file rather than from C#.

### Built

`MinerCraft.Core/Mining/Carve.cs` — `Carve.Sphere` walks the voxels whose **centres** lie
within a radius in metres, removes those whose hardness is at or below the tool's
threshold, and returns a `CarveResult` of three counts. `Carve.Swing` is the same thing
reading its two numbers off a `ToolDefinition`.

`ToolDefinition` plus `tools/*.json` in the loader, `GameContent.Tools`, and two tiers
shipped in `mods/minercraft/tools/`. `MaterialTable` finally projects `Hardness`, which
`MaterialDefinition` has carried since M1 with nothing reading it. `IVoxelEditor` is
`IVoxelWorld` plus a write, and `ChunkPipeline` implements it.

### The mechanic is one comparison, and it explains itself

`hand_pick` has a threshold of 1.0; dirt is 0.8 and stone is 3.0. Swung at a dirt bank
with a stone seam through it, the swing takes the dirt and leaves the seam standing proud.
No special case, no "you cannot mine this" message — **the shape of the hole tells the
player what their tool is for**. That is the whole reason hardness is checked per voxel
rather than per swing, and it is why the base game ships two tiers rather than the one M3
strictly needs: one tier cannot demonstrate a gate.

### Three findings worth keeping

**The distance test is the definition; the bounding box is only an optimisation.** One
rule decides membership, and it is the one the tests check. The box is
`VoxelPos.FromMetres(c ± r)` — the sphere's bounding box in metres, floored into voxel
coordinates. That is provably sufficient: a candidate's centre lies inside the box,
`FromMetres` floors and is monotone, and a centre at `(i + 0.5) × VoxelSize` can never sit
on a voxel boundary, so the rounding would have to be wrong by half a voxel to drop
anything.

The first version hand-derived the tight bound and then widened it a voxel each side as
insurance against float error. Both halves were wrong to keep. The widening defended
against nothing measurable, and the hand-rolled derivation was a *second* flooring
primitive that had to be got right for negative coordinates all over again — when
`VoxelPos.FromMetres` already is, and is already pinned by its own tests. It was also
unfalsifiable in the obvious place: swapping its `Math.Ceiling`/`Math.Floor` for casts can
only ever *widen* the box, because truncation moves toward zero and both ends move outward
when it does. A break-recipe that cannot fail is exactly what this milestone keeps
learning to distrust.

**M3a's "already there" early-out is no longer what protects the carve, and both belong.**
M3-PLAN justified it by "a sphere carve rewrites air as air across most of its bounding
box". The carve now skips air before it ever calls `SetVoxel`, so that path is not reached
from here. Keep both: the carve's skip is what stops it *counting* air as removed — air's
hardness is 0, so without it every threshold would pass and every swing would report
roughly double what it took — and the pipeline's early-out is what protects the next
writer, which is M6's placement.

**A tool radius is a denial-of-service vector, and the loader is where to stop it.** The
scan is cubic, so `"radiusMetres": 1000` does not carve a crater — it stops the game
inside a loop of half a trillion iterations, several layers from the file that caused it.
Rejected at load against `ToolDefinition.MaxRadiusMetres`, and re-checked in
`Carve.Sphere` because the loader is not the only caller. Clamping instead would have
hidden the typo. The bound is 4 m and is **labelled an estimate**; the first number
written there was wrong, and is below.

### Watched them fail, again, and one comment was overclaiming

Every behaviour here was mutation-checked before being trusted, across both passes. The
final set, all confirmed to break the suite: the bounding box narrowed a voxel on the min
side, the radius boundary excluded (`<=` → `<`), the zero-radius early return removed, the
NaN-threshold rejection removed, the air skip removed, unreachable voxels no longer
counted, `Discard` reverted to the pre-M3 "is it `Meshed`?" test, and — not code at all —
the shipped `hand_pick` threshold raised past stone.

The one that earned its keep was on the *pipeline* test. Its comment claimed the "eight
chunks remeshed, once each" assertion caught the border rule failing. Reducing
`ChunkEdits.DirtyChunks` to owner-only left it **green** — the writes already span eight
chunks by themselves, so the border rule was never load-bearing there. Fixed by saying
what the test actually proves (coalescing, and no outward spill) and adding a corner test
that writes a *single* voxel — which does fail when the rule is reduced. **Three for three
now: a green test is not evidence that its comment is true either.**

### A second pass caught three defects the first one shipped

The implementation was reviewed by an independent panel working from M3-PLAN and the code,
which found three things a green suite had not.

**`ToolDefinition.Unknown` was not inert, and its own doc comment said it was.** Radius 0
with no early return still met the distance test whenever a swing landed exactly on a
voxel centre, and a threshold of 0 then took anything of hardness 0 — which is air today
and fluids from M8. The test that was supposed to pin this passed for the wrong reason: it
swung at `Vector3.Zero`, which is a voxel *corner*, so voxel 0's centre was 0.216 m away
and nothing was ever in range. Now inert twice over — a non-positive radius returns early,
and the threshold is negative infinity — and the test aims at a voxel centre.

**`MaxRadiusMetres` did not bound the failure its own comment named.** 16 m is a 64-voxel
radius: ~2.2 million box iterations and up to 729 chunk clones, which turns a hang into a
repeated multi-hundred-millisecond freeze rather than preventing one. The comment also
called it "two chunks across" while stating the radius, and a chunk is 8 m, so 16 m is
four chunks of diameter. Now 4 m, **labelled an estimate** rather than written as a fact,
with M3d's held-button measurement named as what should set it.

**`MaterialTable.ForTesting` had a fail-open default.** An optional `hardness` array
defaulting to all zeros means "any tool mines this", so a carve test that forgot the
argument would pass for the wrong reason. Required now; three call sites say what they
mean.

### Then the review found a bug the carve only made reachable

**A viewer leaving while an edit remesh is in flight stranded the mesh on screen.**
`OnChunkUnviewed` discarded a chunk's mesh when its state was `Meshed`. That was
equivalent to "does the renderer hold a mesh for this?" until M3a added the `Meshed →
Meshing` edge — before it, a chunk that was `Meshing` had never delivered anything. After
it, a chunk part way through a *re*-mesh has had a mesh drawn since before the edit, and
that is exactly the one nothing else would ever take down: `Advance` refuses a chunk
nobody views, and `Complete` skips delivery when the view count is zero. Mine something,
fly away while the remesh runs, and the pre-edit geometry keeps drawing for terrain that
has been unloaded.

Fixed with `StreamedChunk.HasLiveMesh` — a flag on the thing it describes, the same shape
as `InWorkList` — rather than by widening the state test, because widening would call the
"a chunk that had a mesh is gone" callback for chunks that never had one.

**Also: `hardness > NaN` is false, so a NaN threshold inverts the gate** from "takes
nothing" to "takes everything, bedrock included". The loader already rejected non-finite
thresholds, but the loader is not the only caller, so `Carve.Sphere` rejects it too and
the message names `float.NegativeInfinity` as the supported way to say "takes nothing".

### The base game's own files are finally loaded by a test

Every content test until now built an `InMemoryContentSource`, so the files the game
actually ships had never been read by one — the wrong gap to have under non-negotiable 5,
where the whole claim is that the base game is a mod. `ShippedContentTests` reads
`MinerCraft.Game/mods/` from disk and checks the *numbers*: that some shipped tier leaves
the hardest material standing and some tier can take it. The hardness gate is a property
of the content, and no amount of testing the code finds a JSON file edited to make it
meaningless.

The fixture had already drifted, incidentally — `TestContent.BaseGame()` has no grass.
That is now written down as deliberate rather than left to look like an oversight: the
fixture is minimal so `IdStabilityTests`' numeric assignment stays stable, and the shipped
content has its own test.

### One flake, not ours

`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` failed once while
about thirty review agents were saturating the machine, and passes every time since. It is
a one-worker scheduler test with a wall-clock `Patience`, so it is load-sensitive by
construction. Recorded in TODO rather than fixed — it is not M3c's, and a timeout that
only fires under a fully loaded box is a real if minor trap for CI later.

### Smaller things

- **`CarveResult.Unreachable` is separate from `TooHard`** for the same reason
  `RayOutcome` keeps `Blocked` separate from `Missed`. A swing at a wall and a swing off
  the edge of the loaded world produce the same-looking hole and are not the same event.
- **`ToolDefinition.Unknown` is inert where `MaterialDefinition.Unknown` is solid.**
  Opposite choices, one rule: pick the failure that cannot destroy a world. A missing
  material must stay solid so the ground does not vanish; a missing tool must carve
  nothing so a save cannot be hollowed out by numbers nobody has any more.
- **A hole in the material table now gets `UnknownHardness` rather than 0.** It was
  already forced opaque; leaving hardness at zero would have made a removed mod's blocks
  the easiest thing in the world to tunnel through.
- **`CooldownSeconds` is on the tool with nothing reading it.** M3d binds the mouse and
  will want it, and adding a field to a format mods have written against is a migration —
  the same argument `MaterialDefinition`'s unused fields already make.
- **No `savedToolIds` seeding.** A material's numeric ID is baked into every stored voxel;
  a tool's is referenced by nothing on disk yet. M6's inventory is when that changes, and
  it will be the same three lines.
- Headless from a standing start is unchanged — `1 mod(s), 4 material(s), 2 tool(s), 0
  error(s)`, then 1926 jobs done, 165 drawn.

## 2026-08-29 — M3b: the raycast, and an oracle that was worse than the code

307 tests passing, up from 294.

### Built

`VoxelRay.Cast` — Amanatides & Woo — plus `IVoxelWorld`, which `ChunkStreamer` now
implements. It returns the hit voxel, the face normal, the entry distance, and the last
empty voxel before the hit; that last one is where a placement goes at M6, so returning it
now costs nothing and saves reopening this.

`RayOutcome` has three values, not two. **`Blocked` is separate from `Missed`** because an
unloaded voxel is an unknown rather than an empty one — collapsing them would let the
player aim through the edge of the loaded world at a voxel in a chunk that does not exist,
where the
edit then vanishes with nothing to report.

### The finding: a sampled reference is not an oracle

The plan said to fuzz against a brute-force reference, the way `ChunkBox` was fuzzed. The
obvious reference is a sampler — step along the ray far finer than a voxel and report the
first solid one. Written that way, the fuzz **failed on the DDA being right**.

A ray can clip the corner of a voxel and stay inside it for a shorter distance than the sample
step, so the sampler misses voxels the DDA correctly reports. With 4,000 rays and a step of
1/64 of a voxel that is not rare at all. **The reference was less exact than the thing it was
testing**, which makes it not a reference.

Replaced with a ray/AABB slab test against every solid voxel, keeping the nearest entry. That
is exact, and it is a genuinely different algorithm — intersect each box, versus walk the grid
incrementally — which is what makes agreement worth anything. Sharing reasoning with the
subject is the other way an oracle quietly stops testing.

### Watched it fail, deliberately, before trusting it

The M3a lesson applied on the next part rather than filed and forgotten. Flipping
`FirstBoundary` to use the near corner for positive directions breaks the fuzz in **7 ms**,
so it has teeth. Both facts are in the test's comment, because the next person to touch it
should not have to redo the experiment to know it was done.

Two for two now: M3a's fuzz hung instead of failing and had to be fixed, and M3b's first
oracle was wrong in the subject's favour. **Neither would have been noticed by a green test
run.**

### Smaller things

- **Axis-parallel rays work by arithmetic rather than by special case.** An axis the ray does
  not move along gets an infinite `tMax`, so the nearest-axis comparison can never choose it.
  There is a test for it, and its failure mode is the suite hanging rather than failing —
  hence the deliberately short range.
- **A zero direction throws** rather than looping forever, which is the same failure wearing a
  friendlier hat.
- **Stops at the first non-air voxel.** Fluids arrive at M8, at which point "solid" and "not
  air" stop meaning the same thing and this wants a stop predicate. Recorded as a trigger in
  the class remarks rather than guessed at now.

## 2026-08-29 — M3a: the edit path, and a fuzz test that hung instead of failing

294 tests passing, up from 277. Voxels can be written while workers read them, which is the
thing every other part of M3 sits on.

### Built

`ChunkPipeline.SetVoxel` writes one voxel, clones the chunk first if a job holds it, and
marks every chunk whose mesh that invalidates. `VoxelChunk.Clone`, `ChunkEdits.DirtyChunks`,
three flags on `StreamedChunk` (`VoxelsSharedWithJob`, `NeedsRemesh`, `HasEdits`), and
`IJob.Urgency` feeding band3 of the priority.

**Copy-on-write, and the existing structure did most of the work.** `Neighbourhood()` already
captured `VoxelChunk` *references* on the sim thread — a worker holds a chunk rather than
looking one up — so the whole change is: mark those 27 instances as handed out, and have an
edit clone rather than mutate when it finds the flag set. No lock anywhere on the read path.

### Findings

- **The fuzz test hung on the bug it was written to catch, which is worse than not having
  it.** Deliberately deleting the clone to check the test could fail produced a 10-minute
  timeout, not a failure. Cause: readers bailed out with `return` on the first mismatch, so
  they stopped draining, and the writer blocked forever against a bounded queue. Fixed by
  recording failures and continuing, plus a `try`/`catch` around the copy — because the
  failure that matters most throws rather than returning bad data. It now fails in **356 ms**
  with `snapshot of version 2 held 3`. **A fuzz test nobody has watched fail is a fuzz test
  that might not be testing anything**, and this one had to be watched twice before it was
  worth keeping.
- **Making every edit rewrite the whole chunk is what makes the assertion sharp.** Each round
  fills the chunk with version *k*, so any snapshot must hold exactly one value throughout —
  a torn read shows two, and the check is a comparison rather than a guess about which voxel
  should have changed. `Compact()` at the end of each round demotes back to uniform so the
  next round promotes uniform → palettised again, which is the dangerous churn: it swaps the
  backing arrays and the width fields together, not just a voxel.
- **The no-op guard is not an optimisation, it is most of the work.** `SetVoxel` returns early
  when the material is already there. A sphere carve rewrites air as air across most of its
  bounding box, and without the guard every one of those would clone a chunk and dirty up to
  eight neighbours for nothing.
- **The border rule is fuzzed against its definition rather than a second implementation.** A
  chunk's mesh reads voxels −1..32 of its own space, so the chunks an edit makes stale are
  exactly those whose padded region contains it. The test computes that directly and compares,
  including at negative chunk coordinates where a floor or sign error would hide.
- **Band3 finally has a user.** M2a reserved the top priority band and left it unused through
  all of M2. An edit remesh 40 chunks away now outranks a streaming mesh at the viewer's feet,
  which is the inversion it was reserved to allow. Set to 128 rather than 255, so later work —
  a chunk the player is standing inside — still has headroom above it.

### Honest limits

- **Nothing has been measured.** The clone rate is counted (`ChunkClones`) but no carve exists
  to drive it, so M3-PLAN's "roughly 2 KB per chunk per edit-and-remesh cycle" is still an
  estimate. M3d measures it.
- **A dropped mesh job loses its urgency.** If an urgent remesh is cancelled, the chunk steps
  back to `Generated` and is resubmitted without band3. The mesh is still correct and still
  happens; only the priority is lost, and only for a chunk that was being unviewed anyway.
  Left alone deliberately rather than adding state to carry it.

## 2026-08-29 — M3 planned: mining, and the concurrency bill coming due

No code. [docs/M3-PLAN.md](docs/M3-PLAN.md), split M3a–M3d, following the pattern that made
M2c tractable — split the risky part out *before* starting rather than halfway through.

### The decision that shapes the milestone

**M3a is a concurrency part, and it closes two recorded revisit triggers at once.** M1b said
"no concurrent writer until M3"; M2a said taking the padded copy on the worker thread is safe
"because at M2 nothing writes". Both come due on the same line.

The failure is worse than the obvious one. A torn voxel read would be survivable — the remesh
that follows the edit corrects it. But `VoxelChunk.Set` can **promote the representation**,
uniform to palettised to direct, replacing `_words`, `_palette` and `_direct` wholesale along
with the width and mask fields. A worker part way through `CopyTo` can see `_kind ==
Palettised` against a null `_words`. That is a `NullReferenceException` on a background
thread, so "mostly fine, the remesh fixes it" was never on the table.

**The answer is copy-on-write, and the existing structure already implements most of it.**
`ChunkPipeline.Neighbourhood()` captures `VoxelChunk` *references* on the sim thread at submit
time — a worker never looks a chunk up, it holds one. Add one rule and it becomes a snapshot
scheme: an instance handed to a job is immutable from then on, so an edit clones it and
installs the clone. The worker's reference stays valid and nothing takes a lock. This is the
same shape as M2a's finding that C# references already solve the *lifetime* half for free; it
turns out they nearly solve the mutation half too, given one flag.

Rejected in the plan with reasons: a per-chunk lock (27 chunks per mesh job, held across a
34³ copy, and it gives back exactly the "worker owns its data outright" property M2a paid
for), cancel-and-retry (starves worse the harder the player mines), and a version stamp
validated after copying (the copy itself is what crashes).

### Smaller things worth having decided before starting

- **`band3` of the job priority finally gets used.** M2a reserved the top band for "the player
  is standing on it" overrides and nothing has needed it until now. An edit remesh must
  outrank streaming or the rock you just mined stays on screen while the horizon loads.
- **The border rule is wider than it looks.** A padded copy reaches one voxel into each
  neighbour, so an edit at local 0 or 31 dirties the neighbour on that axis — up to seven at a
  corner. Get it wrong and stale geometry appears along chunk seams, which is exactly where
  mining happens.
- **"Sub-voxel" means the voxel is small, not partly full.** Worth writing down because the
  phrase invites the other reading, and partial occupancy is the two-representations decision
  DESIGN already rejected, arrived at sideways.
- **Edits are lost on unload until M6.** Named loudly so it is not filed as an edit-path bug,
  with a one-bit `HasEdits` flag set now because it is exactly what M6 has to save and is
  awkward to reconstruct later.
- **M3 does not need M4.** Mining binds to the flycam. Putting the first fun moment behind a
  player controller would be sequencing by category rather than by value.

### The question M3 is actually asking

**Does a pick swing fit inside a frame?** M1c measured one chunk meshing in isolation and M2
measured meshing that runs ahead of the player with a streaming system's slack to hide in.
Mining is the first remesh that has to land between the click and the eye noticing. The plan
records the ordered answers if it does not fit — mesh only the dirty region, then smaller mesh
blocks, then accept a frame of latency and hide it — so that "add a lock" does not get
reinvented under pressure.

## 2026-08-29 — The mesh-apply budget, and the spike that wasn't

277 tests passing, up from 274. This closes the last thing M2-PLAN.md specified and M2d
did not build.

### Built

`ChunkPipeline.Update` now applies finished jobs under a time budget — 2 ms by default —
and leaves the rest in the scheduler's channel for the next frame. Supporting it:
`JobScheduler.TryTakeResult` so the caller can stop partway, `PendingResults` on both so a
backlog is visible, an injectable clock so the budget is tested deterministically rather
than raced, and `--applybudget=<ms>` for the A/B.

**Time-sliced here, counted in the free path, and the asymmetry is the point.** Applying a
mesh builds an `ArrayMesh` inside our call, so a stopwatch around it measures the real work.
Freeing one does not, because Godot defers the deallocation — which is why `MeshFreeQueue`
counts instead of timing. Two budgets, two mechanisms, for a reason worth not collapsing.

The budget is checked *after* applying a result, never before. A result costing more than the
whole budget would otherwise be deferred forever and take the queue behind it — a livelock
that would look like the world quietly stopping.

### Measured — r12h/6v, 40 m/s, 8 workers, three runs per default arm

```
budget        update p99   update max   pending at end   frame max
1000 ms       2.04–2.46      3.74–3.91         9–30       6.9–13.1
   2 ms       1.87–2.14      3.28–4.50          8–15      6.9–12.4
 0.5 ms            1.95           4.53             57           7.3
 0.2 ms            2.11           4.68            475          32.3
```

1000 ms is "unbudgeted" — a number large enough never to bind, rather than a special case in
the code.

### Findings

- **The budget binds, and the instrumentation catches it binding.** At 0.2 ms the backlog
  reaches 475 results and `tracked` runs ahead of `viewed` — 14,564 against 14,297 — because
  chunks are being retained waiting for results that have not landed. That is exactly the gap
  CLAUDE.md says never to ignore, appearing on demand. At the 2 ms default the backlog stays
  under 30 and the gap never opens.
- **A time slice cannot bound the tail below the cost of one item, and this is the honest
  limit of the whole approach.** Update max sits at 3.3–4.7 ms in *every* arm, including at
  0.2 ms. One mesh conversion is atomic; no budget subdivides it. So the budget bounds the
  frame's *average* work and its p99, and the true worst case is set by the largest single
  mesh. Bounding that would mean capping mesh size or splitting the conversion, which is not
  worth doing on this evidence.
- **The 27.6 ms frame from yesterday does not reproduce, and it is not mesh application.**
  Across six runs the frame max ranged 6.9–13.1 ms with no relation to the budget, and the
  single worst frame measured all session — 32.3 ms — appeared in the *most tightly budgeted*
  arm. Yesterday's entry already flagged that sample as outside the instrumented section;
  this settles it. **Frame time in headless is the wrong instrument** — M2d-1 found the same
  thing and it is worth writing down twice.
- **So this is a null result on the motivation and a pass on the specification.** The change
  does not fix a stutter, because there is no reproducible stutter here to fix. What it does
  is put a bound on a genuinely unbounded cost, at no measurable price: mean update time,
  main-thread share and drawn counts are indistinguishable between the 2 ms and 1000 ms arms.
  The bound is worth having before M3 makes remeshing continuous rather than after.

### Honest limits

- **The default of 2 ms is a judgement, not a measurement.** Applying results averages 0.44 ms
  and reaches 2.26 ms at p99 unbudgeted, so 2 ms clips roughly the worst 1% of frames and
  leaves everything else alone. That is where a safety rail belongs, but nothing measured
  says 2 rather than 3.
- **Nothing here was measured windowed.** Whether any of this is visible to a player still
  needs the windowed run that M1d's draw-call questions have been waiting on since M2d-1.

## 2026-08-28 — M2d-2: the cave optimisation, and a three-times-wrong baseline

274 tests passing, up from 261. M2 is done.

### Built

Cave noise is now sampled on a lattice every `CaveLatticeStep` voxels (default 4) and
interpolated in between, so a chunk costs 729 fBm evaluations instead of 32,768. Also
`MinerCraft.Bench`, a repeatable worldgen harness over the same 2,048-chunk region M2b
measured, and `--cavestep=N` on the game so the same A/B runs inside the streaming loop.

Three design points worth not undoing:

- **One definition of the field.** `IsCaveAt` was rewritten to resample the same lattice
  rather than keeping per-voxel evaluation beside it as "the exact one". The generator reads
  a buffered lattice and the query interpolates eight fresh corners, and they agree *bit for
  bit* — same operations in the same order, which is why the test is an equality and not a
  tolerance. Two definitions of the same field drift, and the symptom is mining a wall that
  is already air.
- **The step must be a power of two dividing 32.** That is what makes a chunk boundary a
  lattice point, and therefore what makes independently generated neighbours interpolate
  from the same corners. Nothing detects a violation at runtime — the damage is a seam — so
  the constructor throws.
- **Linear interpolation, not the quintic fade from `GradientNoise`.** The fade exists to
  shape a field out of gradients, where flattening at lattice points is the whole point.
  Reusing it to *resample* an already-smooth field is a first-order error, because smoothstep
  is flat where the true field is not; plain linear is second-order.

### Measured — Release, 2,048 chunks, best of 5

```
config       ms/chunk   caves   storage  uniform    carved      lost    gained
caves off        0.38       —   2.7 MiB      82%         —         —         —
step 1 (exact)   2.23    83 %   3.9 MiB      68%   1.444 %         —         —
step 2           0.68    44 %   3.9 MiB      68%   1.412 %   0.033 %   0.001 %
step 4           0.47    20 %   3.9 MiB      69%   1.292 %   0.153 %   0.001 %
step 8           0.43    13 %   3.8 MiB      70%   0.903 %   0.544 %   0.003 %
step 16          0.42     9 %   3.4 MiB      74%   0.245 %   1.207 %   0.007 %
```

`carved` is the fraction of rock removed; `lost` is rock the exact field carved and this step
left solid; `gained` is the reverse.

### And in the game — r12h/6v, +2 margin, 8 workers

```
speed    step   drawn   jobs done   dropped    main    update p99   memory
40 m/s      1     145      19,738    30,792   2.19%       1.14 ms   30.6 MiB
40 m/s      4     731      52,120    16,188   6.13%       2.04 ms   54.7 MiB

 6 m/s      1    1594      20,524     4,674   3.64%       1.83 ms   91.9 MiB
 6 m/s      4    2247      30,113     1,301   4.68%       2.12 ms  118.1 MiB
```

The 6 m/s pair is the median of three runs each. Spread was tight: step 1 gave 1594/1562/1592
drawn, step 4 gave 2247 three times.

### Findings

- **M2b's headline `6.55 ms/chunk` was a Debug number, and nothing recorded that.** Debug
  today measures 7.88 with caves and 1.22 without; Release measures 2.23 and 0.38. The *cave
  share* M2b reported — 80% — reproduces in both builds, so the finding it drove was sound
  and only the scale was wrong by roughly 3×. The game project builds Debug, so every
  streaming profile in this worklog is Debug and self-consistent; it was only the generation
  figure that was quoted as if it were the shipped cost. The bench now prints a loud banner
  when built Debug, because a Debug number is worse than no number: it looks like a
  measurement.
- **Interpolation only ever removes caves — never invents one.** Predicted from the
  mechanism, then measured rather than assumed. Linear interpolation between two samples
  cannot exceed the larger of them, so a noise peak sitting inside a lattice cell gets
  clipped, and the cave threshold sits near p95 — it selects exactly the peaks that get
  clipped. The `gained` column is 0.001–0.007% at every step, i.e. nothing. The whole
  disagreement is one-directional.
- **So the step is a cave-volume knob, not an accuracy knob, and reading it as accuracy would
  have picked the wrong default.** By voxel disagreement, step 8 looks nearly free at 0.54%.
  By cave volume it removes 37% of the caves (1.444% of rock carved, down to 0.903%). Step 16
  removes 83%. Step 4 costs 10%, which is the trade actually taken. The two columns exist in
  the bench because the first one alone is misleading.
- **Five times as much terrain on screen at 40 m/s**, at the same view distance and speed.
  Jobs completed went up 2.6× and jobs dropped nearly halved. This is M2d-1's prediction
  landing: it said generation throughput, not box size, caps sustainable view distance.
- **Main-thread cost went *up*, and that is the confirmation rather than a regression.**
  2.19% → 6.13% of one core at 40 m/s. Nothing about the box changed; the main thread was
  idle waiting for workers and is now applying 2.6× more results. M2d-1 measured main-thread
  cost as "bounded by worker hand-back rate, not box size" and this is the same statement
  from the other side — feed it faster and it rises.
- **At walking speed the win is a different shape: it removes marginality rather than adding
  chunks.** Step 4 draws 2,247 in all three runs — the same number every time, which is what
  saturation looks like: every chunk in the visible box that has a mesh has one. Step 1 sits
  at ~1,580 and does not saturate. Notably M2d-1 recorded 2,245 drawn and 122.1 MiB for this
  config, which matches *step 4* today, not step 1 — so 6 m/s was already on the boundary for
  per-voxel caves, and which side a run landed on depended on what else the machine was
  doing. Step 4 is comfortably clear of it.
- **The next bottleneck moved, and it is the one already written down.** Update p99 went
  1.14 → 2.04 ms and one frame hit 27.6 ms where the old build's worst frame was 6.9. Applying
  finished meshes is still unbudgeted — `ChunkPipeline.Update` bounds submissions, not
  `ArrayMesh` construction — and it only stayed invisible while generation was slow enough to
  meter it. That TODO item goes from "recorded" to "next".

### Honest limits

- **The 27.6 ms frame is one sample and lands outside the instrumented section** — update max
  was 5.43 ms in the same run. It is consistent with unbudgeted mesh application plus the GC
  churn behind it, but headless has no renderer, so attributing it properly needs the windowed
  run that M1d's draw-call questions are already waiting on.
- **The threshold was tuned at M2b against the exact field and has not been re-tuned.** Step 4
  carves ~10% less than the value 0.42 was chosen to produce. Fixing that means nudging
  `CaveThreshold` down, which is a worldgen tuning decision; bundling it into an optimisation
  would have meant neither was measured. Recorded in TODO.md instead.
- **Cross-session absolute numbers are not comparable** — the r12h/6v figures here differ from
  M2d-1's for the same nominal config. Within a session the spread is about 2%. Every claim
  above is an A/B taken back to back.

## 2026-08-27 — M2d-1: the unload path, and measurement that corrected a decision

261 tests passing, unchanged — this part is a Godot-layer change plus instrumentation.

### Built

`MeshFreeQueue` (budgeted mesh release), `StreamingProfiler` (percentile sampling), a
scripted surface-following flythrough, and command-line overrides — `--profile`,
`--radius`, `--vradius`, `--margin`, `--speed`, `--warmup`, `--measure` — so a sweep is one
shell loop rather than five edit-rebuild cycles. That cheapness is what made the finding
below possible at all.

### The load margin is not a tuning knob, and three documents said it was

The headline, and it reverses a claim written yesterday. M2c-1 recorded that "while stage 2
places nothing, a margin of 1 produces identical terrain for 30% fewer chunks", and made
the margin a constructor parameter so M2d could pick the cheaper one.

It is wrong. **The barrier is enforced whether or not stage 2 places anything**, so dropping
the margin does not buy cheaper terrain — it shrinks the *meshable* radius by one on every
axis. Measured:

```
margin 1, r4h/2v   →  847 tracked, 97 drawn, 1.37% of one core, 28.7 MiB
margin 2, r3h/1v   →  847 tracked, 97 drawn, 1.37% of one core, 28.8 MiB
```

Identical on every number collected. Margin 1 at a given radius simply *is* margin 2 at one
radius less. There was never a 30% saving to take: the same saving, with the same output, is
available by lowering view distance and keeping the margin correct for when features exist.

Corrected in `ChunkStreamer`, M2-PLAN.md, DESIGN.md and CLAUDE.md. The parameter stays, but
now documented as a test seam rather than a knob.

### Measured: view distance, flying at 40 m/s

```
config    chunks   drawn   update p99   main    jobs dropped   memory
r2h/1v       567      49      0.82 ms   0.97%           127    27.2 MiB
r4h/2v      1521     189      1.20 ms   2.50%           712    32.3 MiB
r6h/3v      3179     249      1.71 ms   3.34%          5166    35.5 MiB
r8h/4v      5733     283      1.26 ms   3.13%         14061    35.2 MiB
r10h/5v     9375     307      1.60 ms   2.94%         30266    35.7 MiB
r12h/6v    14297     205      1.24 ms   3.11%         56132    31.5 MiB
```

And the same large radii at a realistic speed:

```
r10h/5v at 6 m/s    9375    1462      1.21 ms   1.67%    2810    86.8 MiB
r12h/6v at 6 m/s   14297    2245      1.42 ms   2.97%    5244   122.1 MiB
r12h/6v at 15 m/s  14297     759      1.64 ms   3.24%   15016    56.0 MiB
```

### Findings

- **Main-thread streaming cost plateaus at about 3% of one core**, flat from r6 to r12
  across a 4.5× range in chunk count. It is bounded by how fast the workers can hand back
  results, not by how big the box is — so view distance is not a main-thread cost at all.
  Update p99 stays near 1.2–1.7 ms throughout.
- **The drawn count falling at r12 was a speed effect, not a ceiling, and I nearly wrote it
  up as one.** At 40 m/s r12 draws 205 chunks against r10's 307, which reads exactly like
  "past the useful limit". At 6 m/s the same r12 draws 2,245 and sits at 2.97% of a core.
  The sustainable view distance is set by the ratio of movement speed to generation
  throughput, and nothing about the radius itself. Worth having run the control.
- **Memory is mesh-dominated: ~46 KiB per drawn chunk against ~2 KB of voxel storage.**
  From the 6 m/s pair, 783 extra drawn chunks cost 35.3 MiB. This is the useful shape:
  horizontal radius is expensive because it adds *surface*, while vertical radius is nearly
  free because it adds sky and buried rock that mesh to nothing. DESIGN.md argued for
  separate radii from instinct; this is the number behind it.
- **Caves are what cap view distance.** At 6.55 ms/chunk with caves as 80% of it, generation
  throughput is the constraint that makes r12 fall apart at speed. The optimisation deferred
  from M2b is therefore not a tidy-up — it is the thing that would raise the sustainable
  view distance, which is exactly the setting a player will reach for.
- **`VoxelRenderInstance` never disposed its `ArrayMesh`.** It freed the instance RID and
  left the mesh to the GC finaliser — so the expensive half, the vertex-buffer deallocation
  that this whole part exists to control, was happening at times nobody chose. Budgeting the
  frees without fixing that would have controlled the cheap half, measured no improvement,
  and been thoroughly confusing.
- **Frame delta does not measure streaming, and the first profiler said so loudly.** It
  reported mean, p50, p95, p99 and max all within 0.05 ms of each other — not uniform work,
  but a main loop paced by something that is not us, with our costs invisible inside it.
  Replaced with direct timing around `ChunkPipeline.Update` and the free queue, which is the
  number that actually predicts stutter.

### The honest limit of this measurement

**The free queue's benefit cannot be demonstrated headlessly**, because headless Godot has
no renderer and therefore no deferred Vulkan deallocation to spread out. What the runs do
show is that the budget is not itself a bottleneck: peak queue depth never exceeded 27 in
any configuration, so nothing backs up. The queue is justified by the mechanism and by
godot_voxel's experience of the same problem, not by a number produced here. Confirming it
needs a windowed run, and belongs with the next visual check.

### Suggested view-distance presets

Grounded in the table above, at walking speed, and headless — so these carry no rendering
cost yet:

| Preset | Radii | Chunks | Memory |
|---|---|---|---|
| Low | r4h/2v | 1,521 | not measured at this speed |
| Medium | r8h/4v | 5,733 | not measured at this speed |
| High | r10h/5v | 9,375 | 86.8 MiB |
| Ultra | r12h/6v | 14,297 | 122.1 MiB |

Only the last two were measured at 6 m/s; the sweep ran the smaller radii at 40 m/s, where
too little gets meshed for the memory figure to mean anything. Interpolating from ~46 KiB
per drawn chunk would give plausible numbers for the first two, which is exactly why they
are left blank — a figure that looks measured and is not is worse than a gap.

## 2026-08-27 — M2c-2: wiring streaming to the scheduler

261 tests passing, up from 254. **M2 now runs end to end**: terrain streams around the
flycam through the job queue and the greedy mesher, and `Main.cs` no longer builds a chunk
by hand.

### Built

`ChunkPipeline` (work list, job submission, result application, cancellation),
`GenerateChunkJob` and `MeshChunkJob`, `PaddedChunk.CopyFrom` for the worker-side
neighbourhood gather, chunk retention in `ChunkStreamer` while a job is in flight, and the
Godot half — mesh handover and RID freeing.

### Measured, headless, from a standing start

```
r4h/2v + 2 margin, 8 workers
1521 viewed (1521 tracked), 166 drawn, 0 in flight, 0 queued
1926 jobs done / 0 dropped / 0 faulted   — settled inside 5 s
```

**1926 = 1521 generates + 405 meshes, and 405 is exactly the 9×5×9 visible box.** That is
the M2c-1 margin property holding in the real pipeline rather than in a test that drove the
stages by hand: the margin generated, and not one margin chunk meshed. `viewed == tracked`
is the other half — nothing was retained after its last viewer left.

166 of 405 meshed chunks produced geometry at all. The rest are open sky or buried rock,
which is the uniform fast path from M2b paying out again.

### A work list, not a per-frame scan

Deciding what each chunk needs by sweeping all of them is O(n) per frame with n in the tens
of thousands. Instead a chunk is pushed exactly when something might have changed for it —
it was viewed, its job reported, or a neighbour advanced a stage — with the dedup flag on
the chunk so pushing twice in a frame costs nothing.

The subtle part is that **advancing one chunk has to push 26 others**. A chunk finishing
terrain may be the last thing its whole neighbourhood was waiting on before any of them can
run their feature stage. Miss that push and the pipeline stalls silently with everything
idle and nothing wrong — which is the worst shape of bug this milestone could have had.

### Findings

- **`Queued` and `Generating` are only distinguishable because submissions are budgeted.**
  This nearly got collapsed into one state: the sim thread cannot observe a worker picking a
  job up, so "queued" and "running" look identical from here, and an unreachable state is
  exactly the contradiction the enum exists to prevent. What rescues it is
  `maxSubmissionsPerFrame` — a chunk that wants generating but has no slot this frame sits in
  `Queued`, and one with a job in flight is `Generating`. Both reachable, both meaningful,
  and the six states from the plan survive intact. Worth knowing before anyone "simplifies"
  them.
- **`onChunkViewed` has to fire on revival, not just on creation.** A chunk retained past its
  last viewer keeps whatever state its cancelled job left it in. If coming back only
  incremented the refcount, that chunk would sit in `Generating` behind a job that was
  already cancelled and never be looked at again — a permanent hole exactly where the player
  turned around. Now the callback fires on every 0→1 transition and says "wanted", not
  "created".
- **The fault path got simpler after the retry counter turned out to be untestable.** The
  first version capped retries at three. With `WorldGenerator` a concrete dependency there is
  no clean way to induce a fault, and an untestable branch guarding against a spin is worse
  than the simpler behaviour: report it, step back, do not retry. A deterministic bug — which
  is what a fault here is — reaches the same end state either way, and leaving and returning
  retries it naturally. Deleted the counter and the dictionary behind it.
- **Under fast movement nine jobs in ten are cancelled, and that is the system working.**
  The fuzz measured 394 completed against 3,693 dropped. Recorded as an assertion, because a
  version of this test that quietly stopped exercising cancellation would still pass every
  other check in it. A real player crossing one boundary at a time turns over a 49-chunk
  shell rather than a 343-chunk box, so this ratio is the stress case, not the normal one.
- **The mesh job gathers all 26 neighbours although only 6 are readable today.** Face
  visibility never reads a border voxel with more than one coordinate out of range, so edges
  and corners are dead weight — until M5, where vertex AO samples diagonally and a corner
  voxel is precisely what it reads. 392 extra voxels against 6,144 is cheaper than revisiting
  the gather later, and the mesher's merge key already carries the AO channel for the same
  reason.
- **Stage 2 runs on the sim thread, deliberately.** It writes into its neighbours by design,
  so putting it on a worker needs a lock over the whole 3×3×3 region — real work for a stage
  that currently places nothing. Revisit trigger is the one already recorded for chunk
  storage: M3, the first concurrent writer.
- **`PaddedChunk`'s own documentation still claimed the copy happens on the sim thread**,
  which M2a reversed a session earlier. Corrected. The class doc was the last copy of a
  decision that had already been superseded in three other places.

### Still open

- The unload path frees RIDs immediately. That is M2d's headline item and the one most
  likely to be dismissed until it bites, because the stutter lands a frame or two after its
  cause.
- **Confirmed on screen the same day.** Continuous rolling terrain, no seams, camera above
  ground. The diagnostic worth keeping: the bright and dark slivers on the slopes follow
  terrain *contours, not the chunk grid*, and that is what rules out a meshing or streaming
  seam — a real seam would land on the 8 m lattice. They are terracing risers lit by which
  way each contour segment faces, which is the low-ambient TODO item showing its symptom.
  Terrain scale matches the settings exactly: ~62 m features against a 72 m view box, so
  one hill fills the screen.

## 2026-08-27 — M2c-1: streaming policy, as pure data

254 tests passing, up from 216.

M2-PLAN.md flagged M2c as the part most likely to need two sessions, so it was split before
starting rather than halfway through: **M2c-1 is the policy with no threads and no jobs**,
every property fuzzable against brute force; M2c-2 wires it to the scheduler, which is where
refcounting, cancellation and async completion actually interact.

### Built

`ChunkBox` (half-open box of chunk positions, with the six-way difference decomposition),
`Viewer` / `ViewerId`, `ChunkState` and `StreamedChunk` (the state machine with a guard),
and `ChunkStreamer` (refcounts, box difference on move, the stage barrier).

### The load margin is two chunks, and the second one was not in the plan

The most useful thing this session produced, because it is a design hole that would have
surfaced at M13 as a visible seam rather than as a bug.

A chunk may only mesh once its voxels are final, which means every neighbour that could
write into it has finished stage 2. But a neighbour can only *reach* stage 2 once its own
neighbours have terrain to be written into. That is two rings, not one:

| Ring | Reaches | Because |
|---|---|---|
| `R` | meshable | neighbours out to `R+1` have finished Features |
| `R+1` | Features | neighbours out to `R+2` have finished Terrain |
| `R+2` | Terrain | needs nobody — stage 1 is per-chunk |

The mesher's padded 34³ copy wanted the first ring anyway, so only the second is the stage
pipeline's own cost — but it is a real one: at radius 16 the loaded volume goes from
33³ = 35,937 chunks to 37³ = 50,653, **41% more**. Made a constructor parameter rather than
a constant, because while stage 2 places nothing a margin of 1 gives identical terrain for
30% fewer chunks, and M2d should measure that rather than guess.

> **Corrected the next day — see the M2d-1 entry.** M2d measured it, and there was no 30%
> saving to take: the barrier applies whether or not stage 2 places anything, so margin 1 at
> a given radius is exactly margin 2 at one radius less. Left standing here as what was
> believed at the time, which is the point of a worklog.

### The barrier turned out to make a second refcount unnecessary

The obvious way to keep the margin from meshing is to track which shell each chunk is in — a
second refcount over the unexpanded box, kept in step with the first.

None of that is needed. **A chunk in the margin fails the barrier by construction**, because
its own outward neighbours were never loaded at all. Running the pipeline to a standstill
lands the meshable set exactly on the viewer's visible box, from one refcount and a
predicate. `TheMeshableSetIsExactlyTheVisibleBox` asserts precisely that, at three radii —
no wider, so nothing draws voxels a neighbour could still change, and no narrower, so there
is no hole at the edge of view.

This is the second time a barrier has paid for itself by deleting bookkeeping rather than
adding it. Worth a bias toward deriving state over tracking it.

### Findings

- **Intersecting before slicing is not a fast path.** Handling the disjoint case up front
  looks like an optimisation and is actually load-bearing: it guarantees the overlap lies
  inside the box on every axis, which is what lets all six slices skip clamping. Written the
  other way round — slice first, clamp as you go — a non-overlapping box emits slabs with
  inverted bounds. Same code, half the length, and correct instead of nearly correct.
- **Fuzz the decomposition against brute force, and assert both halves.** 2,000 random box
  pairs, checking that the emitted sub-boxes are pairwise *disjoint* and that their union is
  *exact*. Disjointness is the half that matters here: a position emitted twice is a
  refcount incremented twice, which is a chunk that never unloads — and it presents as a
  memory leak that isn't one. Exactness alone would not have caught it.
- **The refcount fuzz asserts counts, not set membership.** Set equality passes while counts
  drift, and the two drift directions are different bugs: high never unloads, low unloads
  terrain someone is standing on. Checking the counts costs one dictionary and separates
  them.
- **Self-transitions had to be rejected, and that immediately caught a bug in the unload
  path** — the first run threw, because a chunk viewed and released without ever being
  queued had sat in `Unloaded` the whole time and was being sent there again. A guard that
  fires on its author within a minute of being written is a guard worth having.
- **One test was wrong about the design rather than about the code.** It asserted a fresh
  chunk has `ViewCount == 0`; there is no such moment, because a chunk record only exists
  because a viewer asked for it. Fixed the test and wrote the reason next to it.

### Deliberately left for M2c-2

`ChunkStreamer.Unview` removes the chunk entry the instant the count hits zero. That is
correct while nothing submits jobs and wrong the moment something does — a worker still
holds the chunk and will report a result for something the streamer no longer knows about.
The removal has to become conditional on the outstanding-job count. The comment marking the
exact line is in place, so it is a scheduled change rather than a discovered bug.

## 2026-08-27 — M2b: worldgen

216 tests passing, up from 186.

### Built

`Hash64` (splitmix64), `GradientNoise` (2D/3D gradient noise plus fBm), `StrataTable`,
`WorldGenSettings` / `GenerationStage`, and `WorldGenerator`. Terrain is a pure function of
seed and position: heightmap, strata, caves.

### Measured, over 2,048 chunks — 128 m × 64 m × 128 m

```
4.0 MiB total, mean ~2 KB/chunk, against 128 MiB naive — 32× better
68% of chunks uniform (803 air, 541 solid), 704 palettised, 0 direct
6.55 ms/chunk with caves, 1.35 ms without — caves are 80% of generation
```

That 68% uniform figure is M1b's uniform tier paying off exactly as designed, without
worldgen doing anything clever: only the sky case is an explicit fast path, and the solid
chunks fall out of `Compact()` on the way past.

### Findings

- **Measuring immediately was worth it, because the first number was bad.** 9.48 ms/chunk
  would have made streaming pointless, and nothing in M2c would have revealed *why* — it
  would have looked like a streaming problem. Isolating caves (regenerate the same region
  with `CavesEnabled = false`) took one run and showed they were 84% of the cost.
- **Two of the three obvious cave costs were free to remove.** `Hash64.Combine` chained
  four avalanche rounds, and 3D noise hashes eight lattice corners per octave per voxel, so
  that sits in the innermost loop the generator has; one round over multiply-mixed
  coordinates is ample for choosing among sixteen gradients. And the gradient table was
  indexed with `% 12` — a 64-bit integer division per corner — where padding the table to
  sixteen entries makes it `& 15`. Together: 9.48 → 6.55 ms/chunk.
- **The remaining cost is inherent and deliberately deferred to M2d.** ~256,000
  hash-and-dot operations per chunk is what evaluating 3D fBm per voxel costs. The real fix
  is a coarser cave lattice with interpolation — at frequency 0.02 the field barely varies
  across a 32-voxel chunk anyway — but that is an optimisation project, not a worldgen one,
  and M2b is closed without it. Across ~6 workers this is ~1.1 ms of wall clock per chunk,
  which is fine for building M2c against.
- **The default cave threshold was picked from a distribution, not from intuition.** The
  first guess of 0.55 carved nothing, because normalised fBm at these settings actually
  spans only [-0.48, 0.58] with p99 at 0.50 — "roughly [-1, 1]" is true of the theoretical
  range and badly misleading about the practical one. Measured over 216,000 samples and set
  to 0.42, near p95, for about 5% of underground volume.
- **Changing the hash broke the pinned-constants test, which is the test working.** The
  optimisation altered `Combine`, so the world changed, and the failure said so loudly and
  immediately. That is exactly the alarm that test exists to raise — it just happened to be
  a deliberate change rather than someone reaching for `HashCode.Combine`. Re-pinned.

### Two tests were wrong about the world rather than about the code

Both worth recording, because both were me asserting something tidier than reality:

- **Adjacent columns are allowed to have the same height.** At 48 voxels of amplitude over
  a frequency of 0.004 the terrain is deliberately smooth, so neighbouring columns
  routinely round to the same integer. Asserting they differ tests the rounding, not the
  noise. Now compares distant columns.
- **Caves cluster, so "caves exist" cannot be asserted per chunk.** At the default
  frequency a 32-voxel chunk spans less than one noise lattice cell, so plenty of chunks
  legitimately contain no cave at all — 5% of global volume is not 5% of every chunk. The
  test now separates the two claims by scope: "caves only ever remove material" holds in
  every chunk, "caves exist" is asserted across a 25-chunk region.

## 2026-08-27 — M2a: the job scheduler

186 tests passing, up from 154. 27 of them are the scheduler, all headless in Core.

### Built

`JobPriority` (four byte-bands packed into a `uint`), `JobKind`, `ViewerSet`, `IJob` /
`JobResult`, `JobQueue` (the flat re-sorted list), and `JobScheduler` (dedicated worker
threads, results back through a `Channel`). Nothing submits jobs yet — streaming at M2c is
what will feed it.

### The test that justifies the whole part

`MovingTheViewerReordersAlreadyQueuedWork` submits a near job *first*, then a far one, then
moves the viewer across the map before either runs. A FIFO returns the near job because it
arrived first; the queue returns the far one, because that is now where the player is. That
is the entire argument against `Channels` in one assertion.

### Findings

- **The queue's ordering contract is "after a sweep", and two of my own tests were
  asserting otherwise.** `Enqueue` deliberately does not sort — per-insert ordering is the
  cost this design exists to avoid — so an un-swept queue pops newest-first. Both failures
  were the tests being wrong, not the code, but the property was folklore until they
  failed. Now documented on the class and pinned by
  `BeforeAnySweepTheQueueIsInArrivalOrder`. It is also harmless in practice: the chunk
  queued most recently is usually the nearest one, and the next sweep corrects it anyway.
- **`ViewerSet` came out better than the thing it was copied from.** godot_voxel uses a
  pre-sized never-resized array plus an atomic count, which leaves individual position
  reads able to tear — they accept that, because a torn read only means one job scheduled
  at a wrong priority. We get strictly better for free: reference assignment is atomic in
  .NET, so publishing a whole new snapshot array by swapping one reference gives readers a
  consistent view with no tearing at all. Same lock-free property, no torn reads, and the
  allocation is irrelevant because viewers move at most once a frame. Second time in two
  days that a C# guarantee has made a C++ workaround unnecessary — the first was GC
  replacing `shared_ptr` for chunk lifetime.
- **The fuzz test initially proved much less than it looked like it did.** It submitted 400
  jobs and disposed immediately, so nearly everything came back Dropped from the shutdown
  path rather than actually running — 27 ms for the whole job suite was the tell. The
  exactly-once invariant held, but almost none of the concurrent run/cancel/sweep
  interleaving was being exercised. Now it drains first and asserts that most jobs really
  ran; the suite takes ~290 ms and eight consecutive runs are clean.
- **Caught a callback-under-lock bug in review, before running anything.** `TryTake`'s
  success path fired the dropped-entry callback while still holding the queue lock —
  directly contradicting the comment three lines below it explaining why that must never
  happen. Restructured to collect inside the lock and report outside. This is the kind of
  deadlock that reproduces once a fortnight on a customer's machine.

### Note

Nothing in `MinerCraft.Core/Jobs/` references Godot; the only occurrences of the word are
comments citing godot_voxel. The seam holds, and the tests would stop compiling if it
didn't.

## 2026-08-27 — M2 split into four parts, and DESIGN.md corrected

No code. [docs/M2-PLAN.md](docs/M2-PLAN.md) written: **M2a** job scheduler → **M2b**
worldgen → **M2c** streaming policy → **M2d** unload path and measurement. Same shape as
the M1 split, for the same reason — the four parts fail in completely different ways.

Also pushed to `github.com/MatthewCarven/MinerCraft` today, private, default branch `main`.
The pre-push sanity check passed: `reference/` tracked zero files, and the whole repo is
63 files and 122 KiB, which is itself the proof the 51 MB of godot_voxel stayed out.

### DESIGN.md was still wrong

The Channels correction had gone into TODO.md and the reference notes but **not** into
DESIGN.md, which still listed `System.Threading.Channels` under "Godot 4 / C# specific
traps" as a locked decision. Fixed, and both the FIFO and the priority-heap alternatives
are now recorded under "Rejected alternatives" so neither gets re-litigated. Worth noting
the failure mode: a correction that lands in the working docs but not the decisions doc is
a correction that comes back.

### Two findings from the planning pass itself

- **`HashCode.Combine` would have silently randomised the world.** .NET randomises its
  seed per process, so `hash(seed, cx, cy, cz)` built on it generates *different terrain
  every launch* — while looking perfectly correct within any single session. It fails only
  across restarts, which is exactly when a player would notice their world had changed.
  M2b now specifies splitmix64 or xxHash written explicitly, pinned by a test that asserts
  known constants from a fresh process. This is the kind of bug that would have been
  blamed on the noise function for a week.
- **GC settles the padded-copy question that was reopened yesterday.** godot_voxel needs
  `shared_ptr` so a chunk isn't freed while a mesh job holds it; in C# holding a reference
  already does that, so half their machinery is free for us. What GC does *not* solve is
  mutation — but at M2 there is no concurrent writer, since mining is M3 and fluid is M8.
  So: copy on the worker thread, which keeps the sim thread free and skips the copy
  entirely for cancelled jobs. **Revisit trigger recorded at M3**, where the first writer
  appears and needs either a per-chunk lock or a copy-on-write snapshot.

### Notes on the plan

- **M2c is flagged as likely to spill into two sessions.** Refcounting, in-flight
  cancellation and async completion interact, and the bugs are state-machine bugs that
  only appear under movement. M1c carried the same warning and didn't need it; this one
  probably will.
- Each part gets a single fuzzable conservation invariant, following the M1c lesson that
  one well-chosen property beats several narrow assertions. For the scheduler: every job
  reports exactly one result, never zero, never two. For streaming: the loaded set equals
  the union of viewer boxes, with no chunk waiting on a job that will never complete.
- **Flying fast is the test.** A streaming system standing still proves nothing; the
  measurements in M2d are specified under continuous load and unload.

## 2026-08-26 — Read godot_voxel's scheduling and streaming before M2

No code. Full notes in [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md), with the
file list; TODO.md's M2 section rewritten around them.

**The reference earned its keep a second time.** Before M1b it caught the chunk storage
order. This time it caught that the M2 job queue was specified as the wrong data structure,
which would have been discovered only after the streaming code was built on top of it.

### The catch

TODO.md said "`System.Threading.Channels` job queue for gen + mesh". Channels is FIFO, and
a FIFO cannot do the two things a voxel job queue exists to do: **reprioritise queued work
as the player moves**, and **drop work that became irrelevant before it ran**. godot_voxel
uses neither a FIFO nor a heap — it keeps a flat vector re-sorted on a 200 ms timer, popping
from the back, with the same sweep recomputing priority and removing cancelled tasks.

A priority heap was my next instinct and is wrong for the same reason: it orders correctly
at insert time, and insert time is precisely when the ordering is least valuable. A chunk
queued at 20 m may be 200 m away before a thread frees up. Their `get_priority()` is a
virtual polled on an interval, not a number captured on push.

### Other findings that change the plan

- **Mesh *destruction* is a stutter source and we are unusually exposed to it.** They
  needed a whole third task runner for it, plus a task whose entire body is dropping a
  mesh reference later, because the renderer's deallocation cost is deferred and therefore
  unmeasurable from the caller. Their comment is candid: *"I hate this workaround because
  it feels like we are almost not in control of a stable framerate."* We draw through
  `RenderingServer` with explicit RIDs, so unloading chunks means freeing RIDs in bulk. The
  stutter arrives a frame or two after its cause, so it would have been misdiagnosed as a
  meshing problem. Now an explicit M2 task.
- **Three execution contexts, not one.** Worker pool (priority-ordered), a main-thread
  time-sliced runner (8 ms/frame), and a main-thread "N per frame until done" runner for
  costs you cannot measure. "Off the main thread" is not a sufficient plan.
- **One pool with a `serial` flag, not two pools.** I/O runs serially because it contends
  on shared resources anyway, and a second pool would make disk work compete in a separate
  ignorant queue instead of one ordering. Relevant at M6.
- **Streaming is a box difference over a refcounted viewer set.** Only the shell that
  entered or left view is iterated — O(R²) per boundary crossing rather than O(R³). Chunks
  are refcounted per viewer, which is also how multiplayer avoids being a special case.
  Building the refcount at M2 with one viewer is cheap; retrofitting it is a rewrite.
- **View distance must not be one scalar.** They keep a separate vertical ratio to avoid
  loading useless sky. We probably want that inverted — a mining game wants a tall narrow
  loaded volume, because the content is below the player.
- **Reopened an M1b leftover.** Our note says to build the padded 34³ copy on the sim
  thread when queueing. They hold references to the chunk and its 26 neighbours and copy
  *on the worker*, which keeps the sim thread free and skips the copy for cancelled jobs —
  but requires storage to be readable while the sim thread may write. A real tradeoff, not
  a free win. Deferred to M2 planning rather than decided here.
- **Cancellation is not deletion.** A dropped task still reports back (`TYPE_DROPPED`), or
  the chunk stays flagged as "being meshed" forever and never updates again.

### Patterns worth stealing outright

- Priority packed as four `uint8` bands in one `uint32`, so comparison is one integer
  compare.
- Viewer positions read **without a lock**, deliberately — priority is a heuristic, and
  paying for exactness is waste. Made safe by a pre-sized never-resized array plus an
  atomic count, not by the absence of races.
- Shared config invalidated by **replacement plus a `valid` flag** rather than mutation
  under a lock. In-flight tasks see the stale flag and discard their output. This composes
  neatly with our "registry frozen after load" rule: a reload makes a new frozen registry.
- Thread-local batching of enqueues, flushed once per frame, so the queue mutex is taken
  once per frame rather than once per chunk.

### Where they are unhappy with their own code

Worth recording, because these are the places not to copy:

- Distance-based task dropping is called "not always reliable" with a TODO to replace it
  with cancellation tokens. We should go straight to `CancellationToken`.
- Postponed tasks effectively spin: *"Those postponed tasks will sort of spinlock with no
  sleeping. Is that a bad thing?"* — an open question in their own source.
- A known mutex hotspot when tasks are very short. Our jobs are chunk-sized, so probably
  not our problem, but it says where to look first if the pool underperforms.

## 2026-08-26 — Licence: PolyForm Noncommercial 1.0.0

No code. [LICENSE.md](LICENSE.md) written, with README, TODO and CONTRIBUTORS updated to
match. Matthew delegated the choice.

### The argument

The decision looked like a values question and turned out to be a timing question.

**Loosening a licence is one commit; tightening one is impossible.** Anyone who receives
a copy under MIT keeps those rights permanently, so "start permissive, restrict later if
it gets serious" is not a strategy that exists. The reverse works fine. While one person
holds the whole copyright, the cost of picking too restrictive is therefore zero and the
cost of picking too permissive is unrecoverable. That asymmetry decides it without
needing to predict whether this is ever sold.

PolyForm Noncommercial over the alternatives because it says the actual intent in
readable English — read it, fork it, mod it, share it, don't sell it — in one short file.
BUSL-1.1 was the other serious candidate and has a genuinely nice property (automatic
conversion to Apache-2.0 after four years), but it is drafted for SaaS, and its
Additional Use Grant would need custom wording to make sense for a game.

### Findings

- **Nothing constrains the choice.** Shipped dependencies are Godot (MIT) and .NET 8
  (MIT); xunit and coverlet are test-only and never distributed. `reference/` is
  gitignored and never built against — the comment already sitting in `.gitignore` about
  not inheriting an attribution obligation turned out to be load-bearing.
- **A split code/content licence is premature.** It is the right long-term shape for a
  moddable game — permissive engine, restricted content, so the reusable part is reusable
  and nobody can ship *your* game. But every texture is currently procedural and there
  are no art assets at all, so there is nothing to split yet. Filed as a revisit trigger
  at first real assets rather than done now.
- **GPL was rejected on the mod story, not on principle.** MIT→GPL is legally fine and
  Godot's licence permits it. But whether runtime-loaded mods become derivative works is
  unsettled, and the base game shipping as a mod is a design pillar here. A licence that
  makes third-party mod authors think twice is the wrong licence for this project.
- **The licence is not the moat, and CONTRIBUTORS.md had already spotted why.** It
  records that AI-generated work sits in unsettled copyright territory. A licence can
  only restrict what is protectable, and a meaningful share of this code was written by
  Claude. What actually stops someone shipping a clone is the name and the content, not
  copyright over the engine source. Worth knowing before treating LICENSE.md as armour.
- **The freeze point is the first merged PR, not publication.** Publishing changes
  nothing about the ability to relicense; merging someone else's commit does. That is the
  real deadline, and it is now recorded as the first revisit trigger. A DCO sign-off
  requirement keeps the door open at almost no friction.

### Note on sourcing

The licence text was fetched from the canonical repository and diffed against what was
written, rather than reproduced from memory — a subtly wrong licence is worse than no
licence. Two obvious raw URLs 404'd because that repository's default branch is named
`1.0.0` rather than `main` or `master`. `LICENSE.md` is byte-identical to canonical apart
from the `Required Notice:` copyright line PolyForm expects the licensor to add.

## 2026-08-25 — M1c and M1d: greedy mesher, and a real chunk on screen

154 tests passing. M1c was flagged as the part most likely to spill into two sessions;
it didn't, largely because the test strategy turned out stronger than expected.

### Measured, on a chunk of test terrain

```
chunk stored as palette 4 @ 2b, 8,208 B for 32,768 voxels
naive 196,608 faces → culled 6,128 → greedy 850 quads
7.2× better than culled, 231× better than naive
```

### Built

`PaddedChunk` (34³), `MaterialTable`, `VoxelFace`, `MeshBuffer`/`ChunkMesh`,
`GreedyMesher`, and `ChunkMeshConverter` on the Godot side. The M0 block builder and the
Game-side face table are deleted — Core owns that now.

### Findings

- **The coverage-equivalence test is better than the two invariants it replaces.** The
  plan called for surface-area conservation and a no-overlap check. Expanding every
  emitted quad back into the voxel faces it covers and comparing that to the culled
  mesher's answer *as a set* subsumes both, and additionally catches faces invented in
  the wrong place — which neither original invariant would have. It reads the actual
  emitted vertex data rather than any internal side-channel, so it tests what ships.
- **The U and V axes per face are not free choices.** They must satisfy
  `du × dv = +normal`, or that one face renders inside-out while the other five look
  correct. Derived per face rather than guessed, and pinned by a winding test — the M0
  experience of not being able to check winding without running it was worth avoiding
  twice.
- **Clearing the mask as quads are emitted makes overlap structurally impossible**, not
  merely unlikely. Worth stating in the code, because it is the kind of property that
  gets refactored away by someone optimising the clear loop.
- **`ArrayPool` was the wrong call in the plan.** Pooling earns its keep when a buffer's
  lifetime is unpredictable; here the mesher owns its buffers for its whole life and the
  Godot bridge copies out of them, so plain reuse via `Reset()` is both simpler and
  strictly less work. Steady state allocates nothing either way.
- **The reference implementation in the tests is deliberately a second copy** of the
  visibility rule rather than a call into the mesher's. A reference that shares code with
  the thing it checks proves nothing.
- **Emitting Godot-free output cost one conversion loop and bought the headless tests.**
  That conversion is also what makes buffer reuse safe, since Godot copies rather than
  retaining.

### Note

`stackalloc` inside a nested loop in a test helper tripped CA2014 — a real stack-overflow
risk rather than a style nit. Hoisted out.

## 2026-08-25 — M1b: chunk storage

133 tests passing. `VoxelChunk` holds 32³ voxels in one of three representations, with
the differential fuzzer running 160,000 random operations against a naive `ushort[]`.

### Built

`VoxelChunk` — Uniform (single int, zero allocation) → Palettised (1/2/4/8-bit packed
indices) → Direct (`ushort[]`), promoted as content demands, with `Compact()`,
`Fill()` and a bulk `CopyTo`.

### Findings

- **The 16-bit palette tier the plan called for is pure overhead, and got dropped.** A
  16-bit palette index costs exactly as much as the material ID it points at, so above
  256 entries the palette buys nothing at all and Direct storage is the honest answer.
  Caught while writing `BitsForPaletteSize`; docs/M1-PLAN.md had specified 1→2→4→8→16.
- **Zero-initialised packed storage is already correct on promotion.** When a uniform
  chunk takes its first differing write, the new palette puts the old uniform value at
  index 0 — so a fresh zeroed word array already reads back as the value every voxel
  used to hold. No fill pass, no loop.
- **Every width we use divides 64 exactly**, so the "pad, don't straddle" decision costs
  nothing whatsoever rather than the few wasted bits per word I had expected when
  planning it. The tradeoff turned out to be free.
- **The memory claim needed correcting, not the code.** A test asserted a maximum-entropy
  chunk fits in 16 KB and it came to 16,420 B — the packed array is exactly 16 KB and I
  had forgotten the palette. Worse, I had called that chunk "typical" when every voxel
  being independently random across 9 materials is the 4-bit *worst* case. Split into
  three honest tests: max-entropy (exact bytes), terrain-like (2 bits, ~8 KB), and the
  one that actually explains the budget — **the chunks that dominate a world are entirely
  air or entirely stone and cost zero bytes**. The palette tier only pays for the thin
  shell between them.
- **Compaction is genuinely not eager, and there is a test that says so.** Mining a lone
  block back out leaves the chunk palettised rather than snapping back to uniform,
  because detecting that needs a 32,768-voxel scan and a pick swing must not pay for it.

### Note

`InternalsVisibleTo` added for the test project, so packing internals are tested
directly rather than being promoted to public API just to be reachable.

## 2026-08-25 — Reading godot_voxel, and a storage-order correction

Sources cloned into `reference/godot_voxel` (51 MB, gitignored, repo stays clean). Read
`storage/` ahead of schedule because M1b is next and that is exactly what it covers.

### Corrected: chunk storage order was wrong

They store `[z][x][y]` — **Y contiguous** — with the comment "because it allows faster
vertical-wise access (the engine is Y-up)", and `fill_3d_region_zxy` confirms it
throughout.

M0 chose the opposite, X contiguous, and the justification written down at the time was
*"Y-major keeps a horizontal slab together for the lighting column pass"* — which is
self-contradictory. A column pass wants a **column** contiguous; Y-major makes a
horizontal slab contiguous, which is precisely the opposite.

It matters more here than it would in a generic voxel engine, because nearly every
mechanic in MinerCraft is vertical: skylight propagates down columns, water falls,
structural support propagates up from bedrock, groundwater drains downward, terrain
generation writes a column at a time. All of those would have strided a full 1024-element
slab per step.

Changed to `[z][x][y]`. 100 tests pass, including the bijection test, which independently
confirms the new order is still a valid permutation with no aliasing. Added a test pinning
the exact strides so a future reordering has to argue with the reasoning first.

Cheap today, expensive after M1b — which is the whole argument for having read this now
rather than at M2.

### Confirmed: our palette tier is ours to add

Their `VoxelBuffer` has only `COMPRESSION_NONE` (raw 8/16-bit array) and
`COMPRESSION_UNIFORM`. No palette, no bit-packing. That is correct at their scale — a
16³ block of 8-bit voxels is 4 KB — and would not survive 64× the voxel count. So M1b
takes their uniform fast path and adds the palette tier they do not need, rather than
copying their storage design wholesale.

Nice detail worth stealing conceptually: a uniform channel stores its default value *in
the union slot where the data pointer would be*, so a uniform chunk allocates nothing at
all. In C# that is just two fields with no real saving, but "uniform means no allocation"
is the right invariant to hold.

## 2026-08-25 — Decision: build our own voxel engine

Matthew asked the right question at the right moment: should we just use
[Zylann/godot_voxel](https://github.com/Zylann/godot_voxel) instead of building this?

Decision recorded in
[ADR-0001](docs/adr/ADR-0001-build-our-own-voxel-engine.md): **build our own, read
theirs.**

### Why the timing mattered

Almost nothing was sunk cost. M1a's registry is independent of voxel storage and survives
either way; the coordinate types largely survive; only ~200 lines of block mesh builder
were throwaway. After M1c that stops being true, so this was close to the last cheap
moment to ask.

### What decided it

- **Adopting it would reverse the headless-Core decision.** `MinerCraft.Core` has no
  Godot reference on purpose; godot_voxel puts world storage inside the engine.
- **The game is the simulation, not the terrain.** Phase 2 needs fast direct access to
  voxel arrays plus its own per-voxel fields. A library is worth adopting when it owns
  something peripheral — this would own the thing the product is made of.
- **It is a C++ module and GDExtension is still incomplete**, so realistically we would
  be compiling a custom Godot instead of using the stock 4.7.1 mono build.

### The cost, stated plainly

Six to ten sessions rebuilding something that exists and is better tested, plus giving up
Transvoxel LOD for free. Not a free choice. The justification is that Phase 1 is the
*known* part and the project risk lives in Phase 2, where the library helps not at all.

Revisit triggers are named in the ADR rather than left to mood: M1c or M2 exceeding ~3
sessions each, and M13's LOD work.

### Housekeeping

`reference/` added and gitignored, for third-party sources we read but do not vendor.
Keeps the repo small and avoids inheriting an attribution obligation we do not need.

## 2026-08-24 — M1a: identity and the registry

99 tests passing. The base game now loads from `mods/minercraft/materials/*.json`
through the same code path a third-party mod would use, and the block on screen takes
its textures from those files rather than from a hardcoded enum.

### Built

- `Identifier` — namespaced, validated, lowercase-only.
- `Registry<T>` — seedable numeric ID assignment, freeze semantics, placeholder fill.
- `MaterialDefinition`, `ModManifest`, `IContentSource` (directory + in-memory),
  `ContentLoader`, `GameContent`.
- `mods/minercraft/` with air, stone, dirt, grass.

### Findings

- **The numeric ID space has to belong to the world, not the process.** Assigning
  numbers in registration order at startup looks correct until a mod is installed, at
  which point every number shifts and every saved chunk decodes to the wrong material.
  `IdStabilityTests.WithoutASavedTable_RemovingAModRenumbersEverythingAfterIt` pins that
  failure as a test on purpose, so the hazard stays visible rather than becoming folklore.
- **Reserving air's *number* is not the same as privileging air's *content*.** The
  tension was real: air must be ID 0 for the uniform-chunk fast path, but the dogfood
  rule says no privileged content path. Splitting the two resolves it — the loader
  reserves the number before scanning mods, and the definition still has to arrive from
  `air.json` or it is a load error. That also makes air's ID independent of filename
  ordering, which a test now pins.
- **The placeholder for a removed mod must be solid, not air.** Turning missing blocks
  into air would quietly punch holes through every structure built from them. It is
  opaque, non-replaceable, and retains the original identifier — that last part is what
  makes re-installing the mod lossless rather than merely non-crashing.
- **Load errors are collected, not thrown.** One mod with a malformed file must be a
  diagnosable message, not a black screen. Only a missing air definition is fatal.
- **Load order has to be deterministic even before it is dependency-correct.** Numeric
  IDs for unseen content are assigned in load order, so a non-deterministic scan would
  hand out different IDs on different runs. Base game first, then ordinal by mod ID.
- **A bare `"id": "copper_ore"` resolves into the owning mod's namespace**, so a mod
  cannot accidentally claim someone else's; writing the explicit `ns:path` form is how a
  compatibility patch deliberately targets another mod, and a genuine collision is
  reported rather than silently overriding.

### Deferred

- Dependency declarations are validated for presence but not topologically sorted. Real
  resolution belongs with scripting at M12, when mods can actually conflict.
- `res://mods` resolves to a real directory in the editor and debug builds but not inside
  an exported `.pck`. Packaging loose mod content is a genuine problem; it just is not
  this milestone's problem.

## 2026-08-24 — M0: skeleton

Builds clean, runs headless with no errors, 43 tests passing.

### Built

- `.slnx` solution, three projects. **`MinerCraft.Core` has no Godot reference and must
  never get one** — that absence is the whole multiplayer seam, and a test project that
  exercises Core without Godot is what keeps it honest.
- `VoxelConstants`, `VoxelPos`, `ChunkPos` in Core. Chunk/local decomposition by
  arithmetic shift and mask.
- `Texture2DArray` + `Voxel.gdshader`. Layer index in UV2.x, light×AO in UV2.y.
- Culled block mesh through `RenderingServer`, no `MeshInstance3D` anywhere.
- Flycam, `.gitignore`, `start.cmd` (prefers a vendored Godot, falls back to `GAME/`'s).

### Findings

- **Negative coordinates are the whole reason the coordinate types exist.** Truncating
  division puts voxel -1 in chunk 0 next to voxel 0, which tears a chunk-wide seam
  through the origin. Because chunk size is a power of two, `x >> 5` is the correct
  floor division and `x & 31` the correct positive remainder — both branch-free and both
  already right for negatives. Most of the 43 tests are there purely to pin this down;
  every one of them passes trivially in the positive octant.
- **Godot C# projects don't enable `ImplicitUsings`** (matching `GAME/`), so `System` and
  `System.Collections.Generic` need explicit imports. Cost one failed build.
- **Winding verified analytically, then confirmed on screen.** For all six faces the
  right-hand-rule normal of the emitted triangles points inward, consistently — so the
  block could only render all-correct or all-inside-out, never a mix, and a mixed
  winding bug would have been the nasty one. Matthew ran it and it is correct: Godot's
  clockwise front-face convention is as documented, no flip needed.
- **The screenshots settled four things at once**, which is a good argument for looking
  at the thing rather than only testing it: winding, texture-array layer indexing (three
  distinct materials on one mesh), per-vertex normals (the lit and unlit faces differ
  under `diffuse_lambert`), and the UV2.y shade channel multiplying through at 1.0.
- **The 4×4 tile grid is visible on each face**, which is culled meshing showing its
  working: 16 quads per face, one per voxel. Greedy meshing collapses that to 1 quad per
  face at M1 — 96 quads down to 6 on this shape, a 16× reduction, and the clearest
  possible demonstration of why greedy is mandatory at 0.25 m.
- **Culling maths checks out**: a 4³ solid block emits 192 triangles (six 4×4 faces)
  against 768 for naive per-voxel cubes.

### Deliberately deferred

- Textures are generated in code, so there are no binary assets yet. Real images arrive
  with the registry at M1.
- `RenderingServer` direct vs `MeshInstance3D` is still an assumption, not a
  measurement. The measurement belongs at M2 when there are real chunk counts.

## 2026-08-23 — Design session

Planned the whole thing end to end with Claude. No code written, by request.
Output: [docs/DESIGN.md](docs/DESIGN.md), [TODO.md](TODO.md).

Started as "a Minecraft clone", ended as something meaningfully different.

### Decided

- **Godot 4 + C#.** Chosen over Python/moderngl (GIL caps threaded meshing and render
  distance), JS/three.js, and raw C++/Rust. Godot 4.7 mono is already on disk in
  `GAME/`. Real threads for meshing and worldgen; renderer and input for free.
- **0.25 m voxels, 4 per metre.** 64x Minecraft density, chosen so a pick swing carves
  material rather than deleting a cube. Rejected 0.125 m (512x — lighting and remesh
  become the dominant project constraint) and the hybrid "1 m blocks that subdivide
  when damaged" (two world representations poison the mod API).
- **32³ chunks = 8 m cube.** Small enough that remeshing on a pick swing is cheap.
- **No mobs, no combat, no pathfinding.** Replaced with simulation pressure.
- **Data mods from M1, MoonSharp Lua at M12, C# DLL loading never.** Namespaced string
  IDs are the one decision that makes or breaks it, and it gets made in week one.
- **Multiplayer not now, but not designed out.** Headless sim core plus the event bus
  keeps the seam open.

### The turn that made the project

Claude pitched "the mine is the enemy" — structural integrity and cave-ins instead of
zombies. Matthew took it, kept automation as well, and added **rainfall-driven flooding**
on top.

That third addition is what closed the loop. The three systems aren't three features,
they're one mechanic:

> Rain fills basins → water finds your excavation → saturated rock loses cohesion →
> the ceiling comes down → you build pumps and shoring to fight back → which needs
> power → which needs more mining.

Everything downstream now has a reason to exist. Every machine in the automation tree
fights either water or gravity. Design brief collapsed to one line: **you are a mining
operation fighting water and gravity.**

### Findings worth keeping

- **Greedy meshing barely cares about voxel scale.** Output scales with surface area,
  not voxel count, so the same hillside at 0.25 m merges to roughly the same quad count
  as at 1 m. What gets 64x worse is *scanning* the volume — which is exactly what
  palette compression makes cheap. So greedy meshing goes from "nice optimisation" to
  "mandatory", and pays for itself.
- **Palette + bit-packed storage stops being an optimisation at this scale.** Raw is
  >300 MB, palette-compressed with a uniform-chunk fast path is ~40 MB. Day-one
  requirement.
- **Lighting is the real bill at 0.25 m, and the fix is to not pay it.** Propagate on a
  coarse 1 m grid and sample trilinearly at mesh time — 64x cheaper, and light is
  low-frequency enough that nobody can see the difference. Has to be decided up front
  because it's a mesher and shader decision.
- **Minecraft's fluid model is not worth inheriting.** No volume conservation, never
  equalises, infinite sources, and its stair-stepping would be 4x more visible here.
  Replaced with a three-tier hybrid: analytic watershed basins for rain and standing
  water, volume-conserving cellular automata on an active-cell set for water that's
  actually moving, and a coarse groundwater saturation field for seepage. Different
  scales, different solvers. Sleeping cells are load-bearing, not an optimisation.
- **Saturation degrading support distance is the keystone.** It means water doesn't just
  flood the mine, it *causes the cave-in*. One interaction, two systems, and it's what
  makes the loop feel like a single game rather than a features list.
- **Decouple storage granularity from placement granularity.** Store at 0.25 m, mine by
  carving spheres, but place with 1 m brushes — placing individual sub-voxels would be
  miserable. Falls out naturally into volumetric inventory (litres of stone, not "3
  stone blocks"), which makes crafting costs continuous.
- **Rain occlusion is nearly free.** Reuse the skylight value the lighting system already
  computes to kill particles underground. It's the detail that sells the effect.

### Open

- Godot's `RenderingServer`-direct path vs `MeshInstance3D` nodes needs a real
  measurement at target chunk count, not an assumption.
- Collapse debris representation: loose re-settling rubble (cheap) vs rigid falling
  chunks (better looking, much harder). Deferred to M9.
- Power model complexity — deliberately unspecified until M10.

### Flagged for Matthew

- **Not a git repo.** Wants `git init` before the first code lands.
