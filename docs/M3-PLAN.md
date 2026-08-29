# M3 — Mining

The first milestone whose output is a *feeling* rather than a number. Everything up to here
was infrastructure that could be judged by a benchmark; this one is judged by whether
swinging a pick at rock is satisfying.

It is also where two deferred decisions come due. M1b recorded "no concurrent writer until
M3". M2a recorded that taking the padded copy on the worker thread is safe "because at M2
there is no writer — the revisit trigger is M3". Both of those trigger here, on the same
line of code, and neither is optional.

| Part | What it is | The risk it removes |
|---|---|---|
| **M3a** The edit path | Copy-on-write, dirty coalescing, budgeted remesh, priority override | A worker reading a chunk the sim thread is rewriting — a crash, not a stale voxel |
| **M3b** The raycast | Amanatides & Woo voxel DDA | Picking that disagrees with what is drawn |
| **M3c** The carve | Sphere shape, hardness gating, tools as content | Mining that ignores material, and a tool table that is not moddable |
| **M3d** Binding and feel | Mouse input, target highlight, measurement while mining | Remesh cost staying invisible until it is a stutter |

**M3a is the one most likely to spill into two sessions**, for the same reason M2c did: the
bugs are concurrency bugs, they only appear under load, and they do not look like what they
are. Splitting it out before starting — rather than halfway through — is the move that made
M2c tractable.

---

## M3a — The edit path

### The problem

The sim thread writes voxels while worker threads read them for meshing. Until now nothing
wrote, so the question could be deferred. It cannot be deferred past a pick swing.

### What actually breaks, which is worse than a stale voxel

The obvious worry is a torn read: a worker sees a half-applied edit and meshes one voxel
wrongly. That would be survivable and self-correcting, because the remesh that follows the
edit fixes it.

The real failure is structural. `VoxelChunk.Set` can **promote the representation** —
uniform to palettised to direct — which replaces `_words`, `_palette` and `_direct`
wholesale and rewrites `_kind`, `_bitsPerIndex` and the mask fields with it. A worker part
way through `CopyTo` can observe `_kind == Palettised` against a `_words` that is still
null, or a palette that has been swapped for a wider one mid-loop. That is a
`NullReferenceException` on a background thread, or silently transposed terrain. Not a
voxel out of date.

So "it will mostly be fine and the remesh will fix it" is not available. This needs to be
correct by construction.

### Copy-on-write, which the existing structure almost implements already

Look at what `ChunkPipeline.Neighbourhood()` does today: on the sim thread, at submit time,
it captures **references** to the chunk and its 26 neighbours' `VoxelChunk` objects. The
worker later copies out of those references. A worker never looks a chunk up — it holds one.

That is most of a snapshot scheme already. The missing half is one rule:

> **A `VoxelChunk` instance that has been handed to a job is immutable from that moment on.**

An edit checks a flag on the instance. If it has been handed out, the edit clones it,
mutates the clone, and installs the clone as `chunk.Voxels`. The worker's captured reference
still points at the old object, which nobody will ever write to again. A reference
assignment is atomic, so there is no window and no lock anywhere on the read path.

The flag is set when `Neighbourhood()` captures the reference and cleared on the fresh
clone, so a burst of edits inside one frame clones once and then writes freely.

**Rejected, and why:**

- **A per-chunk lock.** A mesh job holds 27 chunks, so it would take 27 locks and hold them
  across a 34³ copy. Lock-ordering discipline across the whole codebase, contention with the
  sim thread on every swing, and it gives back exactly the property M2a paid to get — that a
  worker owns its data outright.
- **Cancel the in-flight mesh and retry.** Tempting because the machinery already exists:
  `StepBack(retry: true)` does it. But continuous mining would cancel each mesh before it
  finished and the chunk would never redraw. Starvation that gets worse the harder the
  player works is the wrong failure mode.
- **A version stamp validated after copying.** Lock-free and wrong: the copy *itself* is what
  crashes, so validating afterwards is validating that a crash did not happen.

**The cost, stated up front:** roughly one 2 KB clone per chunk per edit-and-remesh cycle. A
swing touching eight chunks is about 16 KB of gen0 garbage. That is a guess about a number,
so M3d measures it. If it turns out to matter, the answer is coarser edit batching, not
locks.

### Dirty chunks coalesce per frame, never per voxel

A sphere carve writes hundreds of voxels across several chunks. Remeshing per write is
absurd, and remeshing per *carve* is still wrong when the player holds the button down. The
edit path marks chunks dirty into a set; `Update` drains that set once per frame and submits
at most one remesh per chunk.

### The border rule is wider than it first looks

A mesh's padded copy reaches one voxel into each neighbour, so editing a voxel on a chunk
boundary invalidates the neighbour's mesh as well as its own. A voxel at local `x == 0`
dirties the chunk at `-1` on X. A voxel in a corner dirties **seven** neighbours.

The rule that is correct without case analysis: dirty the edited voxel's chunk, plus every
chunk whose padded region contains that voxel — which is exactly the neighbours reachable by
the axes where the local coordinate is `0` or `31`. Getting this wrong leaves a seam of
stale geometry along chunk boundaries that only appears when you mine near one, which is
most of the time.

### Priority: this is what band3 was reserved for

M2a packed the job priority into four byte-bands and left `band3`, the highest, "reserved
for 'the player is standing on it' style overrides". An edit remesh must outrank every
streaming job, or the rock you just mined stays on screen while the horizon loads. M3 is
where that band gets its first use, and the reason it was left there.

### Edits do not survive unload, and that is scheduled rather than forgotten

A chunk that unloads and regenerates comes back from the seed, so flying far enough and
returning erases the mine. Persistence is M6 and pulling it forward would be a much larger
milestone.

This is acceptable at M3 **but has to be written down**, or it gets reported as a bug in the
edit path. Two cheap things now that make M6 straightforward:

- **A `HasEdits` flag on the chunk, set the first time it is written.** One bit, free now,
  awkward to reconstruct later, and it is precisely the set of chunks M6 has to save.
- Leave the unload policy alone. Whether an edited chunk should resist unloading is a
  persistence decision, and making it now — with no save file to fall back on — would only
  turn a lost mine into an unbounded memory leak.

### Tests

All of this is Core-only and headless, driven by a synthetic `SetVoxel` — no raycast, no
carve, no Godot.

- **The snapshot holds:** hand a chunk to a job, edit it, and assert the job's captured
  reference still reads back the pre-edit content.
- **The fuzz that matters, and it can be made sharp.** Fill a chunk uniformly with material
  *k*, and have each edit rewrite every voxel to *k+1*. Any snapshot a worker takes must
  then be **uniform** — one version throughout. A torn read shows two versions in the same
  copy, and the assertion is one line. Run it with many edit and mesh threads interleaved.
- **Border dirtying:** edit each of the 27 classes of local position (interior, six faces,
  twelve edges, eight corners) and assert the dirty set is exactly right.
- **Coalescing:** 500 edits into one chunk in one frame produce exactly one remesh job.
- **Priority:** an edit remesh is taken by a worker ahead of a nearer streaming job.
- **The round trip:** edit, let it remesh, and assert the delivered mesh reflects the edit —
  the test that catches a clone installed in the wrong place, which would otherwise look
  like nothing happening at all.

---

## M3b — The raycast

### The problem

Which voxel is the player pointing at, and which of its faces did the ray enter through.

### Amanatides & Woo, not stepping along the ray

Sampling at fixed intervals misses voxels thinner than the step and visits some twice. The
DDA visits exactly the voxels the ray passes through, in order, at two additions and a
comparison per step. It is also the algorithm DESIGN already committed to.

### What it returns

The hit voxel; the **face normal**, which is the axis that stepped last and its sign; the
distance at entry; and the last empty voxel before the hit — which is where a placement
would go at M6, so returning it now costs nothing and saves revisiting this.

### The cases that bite, all of which want a test

- **The ray starts inside solid material.** Return that voxel immediately with no face
  normal. Otherwise the first swing mines whatever the camera is embedded in.
- **Axis-parallel rays.** A zero direction component gives an infinite `tMax`, which is
  correct and works — as long as infinity is allowed to propagate rather than being guarded
  by a divide-by-zero branch that then picks that axis anyway.
- **Negative directions.** The classic place to get a sign or a floor wrong, and it looks
  fine in the positive octant.
- **An unloaded chunk mid-ray must stop the ray, not read as air.** Treating absent as empty
  lets the player target a voxel in a chunk that does not exist, and the edit then vanishes
  with nothing to report. Return "blocked, no hit" and let the caller decide what that means.
- **Range in metres, not voxels**, because tools are specified in metres and converting at
  every call site is how the two drift apart.

### Tests

- Known rays through a hand-built chunk: axis-aligned each way, diagonal, and from outside
  the chunk inward.
- **Fuzzed against a fine-step reference sampler** — a few thousand random rays, asserting
  the same first solid voxel. This is the `ChunkBox`-versus-brute-force move, and it is what
  catches the sign errors that hand-written cases miss.
- The face normal names the face actually entered: stepping one voxel back along it lands on
  the last empty voxel the walk reported.
- Ray starting in solid; ray reaching its range without hitting; ray entering an unloaded
  chunk.

---

## M3c — The carve

### "Sub-voxel" means the voxel is small, not that it is partly full

Worth stating plainly because the phrase invites the other reading. A voxel is one material
or another; there is no partial occupancy. The feeling of carving rather than deleting cubes
comes from the 0.25 m grid — a 0.5 m pick radius is four voxels across.

DESIGN rejected a second, finer representation once already (hybrid 1 m blocks that
subdivide when damaged) on the grounds that two world representations complicate meshing,
collision, saving and the mod API. Partial occupancy would be that same decision, arrived at
sideways.

### The shape is a distance test in continuous space

A sphere centred on the ray's hit point, radius in metres from the tool. Every voxel whose
**centre** falls inside is a candidate. `VoxelPos.CentreMetres` already exists. Cones and
other shapes are later tiers and want the same interface.

### Hardness gates removal, per voxel, and that is the interesting behaviour

A voxel is removed only when its material's hardness is at or below the tool's threshold. So
an early pick swung at a dirt bank with a stone seam in it takes the dirt and leaves the
stone standing proud — no special case, no "you can't mine this" message, just the shape of
the hole telling you what your tool is for. That is the mechanic doing the explaining.

`MaterialDefinition.Hardness` already exists and is already loaded from mod JSON;
`MaterialTable` simply does not project it. One field to add.

### Tools are content, not constants

A tier is `{ radius, hardnessThreshold }` and it loads from `mods/minercraft/tools/*.json`
through the loader materials already use. Non-negotiable 5 says the base game is a mod with
no privileged path, and a tool table hardcoded in C# is exactly the thing that makes a
format wrong. Even with a single tier at M3, the *path* has to be the mod path.

**This is the part to cut if M3c runs long** — one hardcoded tier behind the same interface,
with the loader following in M3c-2. Cutting the interface would be the mistake; cutting the
JSON is only a delay.

### Instantaneous now, progressive damage left explicitly open

Whether a swing removes material at once or accumulates damage per voxel is a feel question
that cannot be answered before the thing exists. It is also not a small change: per-voxel
damage is state chunk storage has nowhere to put, and bolting on a parallel damage volume is
a second world representation again. M3 is instantaneous with a cooldown, and the decision
is recorded in TODO.md rather than made by default.

### Tests

- The sphere removes exactly the voxels whose centres lie within the radius, and no others.
- Mixed materials with a dirt-only tool: the dirt goes, the stone stays, in the same swing.
- A carve straddling a chunk boundary writes correctly into every chunk it touches.
- A carve reaching into an unloaded chunk edits what it can and **reports** what it could
  not, rather than dropping it silently.
- Carving never creates material, and air stays air — the `caves-only-remove` invariant from
  M2b, at a different layer.

---

## M3d — Binding, feel, and measurement

- **Mouse button on the flycam. M3 does not need M4.** The flycam is enough to answer
  whether mining feels good, and waiting for a player controller would put the first fun
  moment behind a milestone that is not about fun.
- **Highlight the targeted voxel** — a wireframe box at the hit position. Aiming at 0.25 m
  voxels without it is guesswork, and every judgement about feel made without it is
  unreliable. This is not polish; it is the instrument.
- **Measure while mining continuously**, not once: chunks dirtied per swing, remesh jobs per
  second, `update ms` under a held button, and the clone rate M3a guessed at. The profiler
  already reports the first three shapes.

### The question this milestone is really asking

**Does a pick swing fit inside a frame?** M1c measured one chunk meshing in isolation, and
M2 measured meshing that runs ahead of the player with a whole streaming system's slack to
hide in. Mining is the first time a remesh has to complete *between the click and the eye
noticing*, and it is the first workload where the answer is a feeling rather than a
percentile.

If it does not fit, the ordered list of answers is: mesh only the dirty region rather than
the whole chunk, then smaller mesh blocks, then accept a frame of latency and hide it. Not
locks, and not a smaller voxel budget.

---

## Order and rationale

**M3a → M3b → M3c → M3d.**

M3a first because it is the dangerous one, everything else writes through it, and it can be
built and fuzzed against a synthetic `SetVoxel` that needs neither a raycast nor a carve.
M2c's lesson was that splitting the risky part out *before* starting is what made it
tractable; this is the same move made deliberately rather than in recovery.

M3b second: a pure function of a ray and a world, fuzzable against brute force, and
dependent on nothing.

M3c third, needing both.

M3d last, because feel cannot be judged before the parts exist and measurement wants the
real thing rather than a harness.

**Deliberately not in M3:**

- **Placement and brushes** — M6. The raycast returns the face normal they need, so nothing
  here has to be revisited for them.
- **Volumetric inventory** — M6. Carving already yields a voxel count, which is a volume.
- **Persistence of edits** — M6. M3 sets the flag M6 reads.
- **Player collision** — M4.
- **Lighting update on edit** — M5. Light and AO are already in the mesher's merge key, so
  a remesh will carry them when they exist and nothing needs rewriting.
- **Progressive break damage** — open, recorded, and needing its own pass if wanted.
