# M5 — Lighting

> Planning doc. See [DESIGN.md](DESIGN.md) for the decisions this implements.
> Written 2026-08-30.

Every job this engine runs is a pure function of one chunk and its immediate neighbours.
Worldgen is a function of seed and position. Meshing takes a 34³ padded copy and touches
nothing outside it. A carve writes a sphere smaller than a chunk. That property is what makes
the whole thing threadable, and M5 is the first milestone that does not have it:

**Light propagates arbitrarily far, and removing one voxel can change light in a chunk that is
not a neighbour.** Open a hole in a cave roof and daylight falls down a shaft two chunks below.
That is not a mesh job's shape, and pretending it is produces the classic voxel bug — a dark
patch that nobody can explain, does not go away, and only fixes itself when the chunk happens
to unload and come back.

There is one number that makes it tractable, and it is worth stating before anything else,
because it is the constraint the rest of the milestone hangs off:

> **4-bit light reaches 15 m. The load margin is 2 chunks, which is 16 m. So a light source
> outside the loaded region cannot reach the visible box, and light computed over the loaded
> region is *exact* inside the drawn one — not approximate, exact.**

The margin already had two load-bearing reasons (the mesher's padded ring, the cross-chunk
feature stage). This is the third, it is the tightest of the three, and it means the margin
is even less of a tuning knob than M2d said it was.

| Part | What it is | The risk it removes |
|---|---|---|
| **M5a** Vertex AO and the merge key | Per-corner AO from the padded chunk, two light channels, four-corner merge key, the diagonal flip | Greedy meshing quietly collapsing towards culled meshing, discovered later when it is ambiguous whether AO or light caused it |
| **M5b** The light grid and propagation | 1 m cells, sky and block BFS, the un-light pass, cell conductance | An incremental update that disagrees with a from-scratch one — the dark patch nobody can explain |
| **M5c** The pipeline | A light stage in the barrier, trilinear sampling at mesh time, re-light on edit | A pick swing that relights and remeshes 64 chunks, five times a second |
| **M5d** Feel and measurement | Does a mine read as a mine, and what did it cost | 1 m cells being too coarse for a 0.75 m tunnel, found after everything is built on them |

**M5c is where the surprises are**, and unlike M4 that is also where they are expected. M5a and
M5b are pure functions in Core with sharp oracles. M5c is where a not-per-chunk algorithm meets
a per-chunk pipeline, a job scheduler, and an edit path that fires five times a second.

---

## The two decisions that set the shape

### 1. Light is computed on the sim thread, not as a job

`ApplyFeatures` already runs on the sim thread rather than a worker, and the recorded reason
is exactly this one: *"It writes into neighbouring chunks by design, so running it concurrently
with anything reading those neighbours needs a lock over the whole 3×3×3 region."* Light is the
same shape of problem, one order of magnitude larger — a BFS whose frontier crosses chunk
boundaries freely and does not know in advance how far it will go.

So: **one global cell queue, drained on the sim thread under a time budget**, writing directly
into the light grids the streamer owns. Cross-chunk propagation then needs no special case at
all, because there is no per-chunk boundary in the algorithm.

**Rejected, and why:**

- **A light job over a 3×3×3 neighbourhood snapshot**, mirroring `MeshChunkJob`. A 3×3×3 window
  is 24 m across, so its half-width is 12 m and light reaches 15 m: a source 14 m away outside
  the window is silently missed, and the symptom is a seam that appears only near light
  sources. A 5×5×5 window fixes the reach and costs 125 chunk references per job. The window
  that is actually correct is "the loaded region", which is not a window.
- **Per-chunk light with iterative boundary exchange.** Converges, but the number of iterations
  is data-dependent, and "how many passes until the light stops changing" is a stopping
  condition that is wrong exactly when the world is interesting.
- **A worker thread with a lock over the light grids.** The grids are 512 bytes each and the
  BFS touches thousands of them; the lock would be held for essentially the whole job. That is
  a worker thread that has become a second sim thread.

**Cost, estimated:** the initial fill at r4h/2v is 1,521 loaded chunks × 512 cells = **779k cell
visits**. At a guessed ~20 ns per visit that is ~15 ms of work, spread across frames by the
budget — a quarter of a second to light a world from a standing start, against the ~0.22 s the
player already waits for the spawn gate. Both numbers are estimates and M5d measures the first.

### 2. Light is sampled per vertex, and that puts it in the merge key

DESIGN says *"sample trilinearly at mesh time"*, and the vertex channel has been reserved for it
since M0. The consequence nobody has written down yet is that **a merged quad only interpolates
between its four corners**, so a quad spanning three light cells ignores the middle one. Merging
is exact only where the light across the run is constant.

That is not a bug to be worked around; it is the rule:

> Two adjacent faces merge iff their four corner shade values match. When they do, the merged
> quad's interpolation is identical to the two separate quads'. Merging is **exact**, not an
> approximation — and where light varies, quads stop merging, which is the price.

The price is what M5a and M5c measure. If it is too high, the named fallback is to take light
out of the vertex entirely and sample it in the fragment shader from a small per-chunk 3D
texture — hardware trilinear, free, and **a light change then costs no remesh at all**, which is
the expensive coupling. That is a real option and it is deliberately not being built first:

- It contradicts a DESIGN line that has held since week one, and DESIGN's rejected-alternatives
  list exists so that reversals are argued rather than drifted into.
- It needs a per-chunk `ShaderMaterial` (Godot's per-instance shader parameters do not take
  textures), which is a material per drawn chunk, or a world-space clipmap volume — either way
  a bigger build than the thing it replaces.
- **AO has to be per-vertex regardless**, so non-negotiable #4's channel is used either way and
  nothing about the vertex format is wasted.
- This project's rule is that the named fallback gets measured before it gets built, and every
  such fallback so far has turned out to be unnecessary.

**Revisit trigger, stated now so it is not a judgement call later:** light in the merge key
costs more than **2×** the quads of AO alone, *or* a single swing into a lit space triggers more
than ~16 chunk remeshes at a rate mining can sustain. Either one and light moves to the fragment
shader.

---

## M5a — Vertex AO and the merge key

> **Built 2026-08-30.** Four things below were settled differently by contact with the code and
> are annotated inline: where the two bytes live and why, what the flip compares, what a face
> against a flat plane actually reads, and which break-recipes are reachable. Measured cost:
> **1.88× the quads**, against the 2–4× predicted here. See the worklog.

### The problem

Give each face vertex a darkening value derived from the solid voxels touching that corner, and
do it without destroying greedy meshing.

### The rule

The standard one, and it is standard because it is right: for a vertex, look at the three voxels
diagonally adjacent to it on the *outside* of the face — the two edge-sharing `side1`, `side2`
and the `corner` between them.

```
occlusion = (side1 && side2) ? 3 : side1 + side2 + corner      // 0..3
ao        = 3 - occlusion                                       // 3 = fully open
```

The `side1 && side2` special case is what makes an inside corner go fully dark rather than
merely two-thirds dark, and it is the difference between a corner that reads as a corner and one
that reads as a smudge.

**The padded chunk is already built for this.** `PaddedChunk.CopyFrom` gathers all 26 neighbours
rather than the 6 that face visibility needs, and its comment says why in as many words: *"They
stop being dead weight at M5: vertex AO samples diagonally, and a corner voxel is exactly what it
reads."* M5a is the milestone that cashes that in. Nothing about the gather changes.

Only `IsOpaque` materials occlude. Water darkening a corner is an M8 question and the predicate
is already the right shape to answer it then.

### Two light channels, not one, and DESIGN is what forces it

Settled 2026-08-30 alongside the sun going away, and it changes what M5a builds.

The reserved channel is one float: UV2.y = light × AO. That is sufficient only while something
else supplies the ambient. With the directional light gone the grid *is* the light, and
**DESIGN's M7 already needs skylight on its own**: *"wet-surface shader term driven by skylight
× recent rainfall"*. A term that multiplies by skylight cannot be handed `sky × block × AO`
pre-multiplied — the skylight is gone by then.

So the vertex carries **two** values, not one:

```
shadeSky   = skylight   × AO        // 8 bits
shadeBlock = blocklight × AO        // 8 bits
```

AO multiplies into both, because AO is geometric darkening and geometry does not care where the
photon came from. That keeps AO out of the shader entirely and out of any later day/night
uniform — which is the second dividend, recorded now so nobody bakes time of day into the
propagated field: **a diurnal cycle is a uniform multiplying `shadeSky`, and costs no
repropagation at all.** Scaling the seed and reflooding would relight the world every frame.

**Where the two bytes live is the one thing to verify before writing the mesher.** Default:
Godot's **vertex `COLOR` array** — four 8-bit normalised channels by definition, so `R = shadeSky`,
`G = shadeBlock`, two spare, no packing arithmetic, no precision question. Costs 4 bytes per
vertex, +8.7% on a quad's ~184 bytes, against mesh memory that is already the dominant term.
The alternative is packing both bytes into UV2.y as `sky × 256 + block`, which is exact in
float32 and **not** exact in float16 — and `ChunkMeshConverter` calls `AddSurfaceFromArrays`
with no flags, so whatever Godot 4.7 compresses UV2 to is what it gets. **Check that first;** if
UV2 survives at full precision the packing saves the 4 bytes, and if it does not, packing is a
silently wrong colour ramp rather than an error.

> **Settled 2026-08-30 by two probes against Godot 4.7.1, and the tie-break above is not the
> reason.** `AddSurfaceFromArrays` with no flags produces a surface *without*
> `ARRAY_FLAG_COMPRESS_ATTRIBUTES` — UV2 stays float32 and the packed value round-trips
> exactly — and a render probe shows no sRGB conversion is applied to `COLOR` under a
> `ShaderMaterial`. So precision does not decide it. **Packing is wrong regardless, because the
> hardware interpolates the packed number:** `unpack(lerp(pack(a), pack(b)))` is not
> `lerp(a, b)`, since the fractional part of an interpolated sky channel lands in the block
> channel's byte. Two corners one sky level apart sweep block light through its entire range in
> between. Per-vertex shade exists *to be interpolated*; a packed pair of independently varying
> channels cannot be. **`COLOR`, and the choice is structural rather than a four-byte trade.**

Either way non-negotiable #4 is satisfied and the M0 reservation did its job: nothing downstream
assumed the channel, so moving it is one line in `ChunkMeshConverter` and one in the shader.

### The merge key, which is the structurally expensive edit

Today: `FaceKey(int Material, byte Light, byte Ao)` — one light and one AO per face.

After: `FaceKey(int Material, ulong CornerShade)`, where `CornerShade` packs the four corners'
`(shadeSky, shadeBlock)` pairs in the quad's own corner order — two bytes each, eight in total.
Two faces merge iff both fields match.

Three things fall out of that, all of them good:

- **`MeshBuffer.AddQuad` takes four shade pairs instead of one value**, and each vertex gets its
  own. That is the whole vertex-side change, and at M5a `shadeSky` is full and `shadeBlock` is
  zero everywhere, so only AO varies — the same "in the key from the start" pattern M1c used.
- **Light drops into the same key at M5c with no further structural change.**
- **Merging stays exact**, per the rule above, and that is testable rather than assertable.

### The diagonal flip

A quad whose corners are `(a, b, c, d)` with `a + c ≠ b + d` renders differently depending on
which diagonal splits it into two triangles — the artefact shows up as a visible crease running
one way along every lit floor. The fix is one line: when `ao[0] + ao[2] > ao[1] + ao[3]`, emit
the other diagonal.

`GreedyMesherTests.EveryQuadIsWoundConsistentlyWithItsNormal` pins the winding today and must
still pass afterwards — the flip changes which diagonal, never which way the triangles face. If
that test can be made to fail by the flip, the flip is wrong.

> **Two corrections from building it, 2026-08-30.**
>
> **The flip compares the total shade, not AO alone.** Identical at M5a — block light is zero
> and the sky channel is AO-monotone — and correct afterwards: at M5c a flat floor with a torch
> on it has *constant AO* and a varying shade gradient, so an AO-only test would never fire on
> the surface most likely to show the crease the flip exists to remove. It stays a pure function
> of the merge key either way, so every face merged into a quad agrees which diagonal it takes.
>
> **"If that test can be made to fail by the flip, the flip is wrong" is unfalsifiable.** That
> test reads vertex *positions*; the flip changes *indices* and nothing else, so it cannot fail
> — which is the right answer for that test and a hole for the flip. Winding the flipped
> triangles backwards is caught by two tests only because
> `BothTrianglesOfEveryQuadFaceOutwardsInEitherFlipState` was added to walk the emitted indices
> and to assert a flipped quad was actually seen.

### What it costs, guessed here and measured before M5b starts

**Estimate, and it is a guess:** AO varies at every convex and concave edge, so flat terrain
merges as before and terraced terrain does not. The integer heightmap's terracing means the
surface has an edge every few voxels. **Predicted 2–4× the quads**, and M2d measured mesh memory
at ~46 KiB per drawn chunk, so mesh memory scales by the same factor: at r4h/2v's 165 drawn
chunks, 7.4 MiB becomes 15–30 MiB. At r12's 2,245 drawn chunks it is 100 MiB becoming 200–400 MiB,
which is the configuration where this stops being free.

The A/B is one flag — `--ao=0` restores the constant-brightness key, exactly as `--cavestep=1`
restores per-voxel cave evaluation. **Take the number before building anything in M5b**, because
it is half of the fragment-shader decision and it is available a week early.

> **Taken 2026-08-30, from a standing start so the chunk set is deterministic and the comparison
> is two numbers rather than two samples of a moving flythrough.**
>
> | | AO off | AO on | |
> |---|---|---|---|
> | r4h/2v, 165 drawn — quads per chunk | 262 | **492** | 1.88× |
> | r12h/3v @ 6 m/s, 1543 drawn — quads per chunk | 253 | 472 | 1.87× |
> | r12h/3v — static memory | 94.8 MiB | 150.4 MiB | 1.59× |
> | r12h/3v — `update ms` p99 | 1.39 | 2.05 | |
>
> **1.88×, against the 2–4× predicted here** — the estimate ran pessimistic, as every estimate in
> this project has. Mesh memory grew by less than the quads because a chunk's memory is not all
> mesh: solving the two r12 runs for a constant baseline gives ~42 KiB of mesh per drawn chunk
> with AO off, which independently reproduces M2d's ~46 KiB, and ~79 KiB with it on. r12's
> 2,245-chunk case extrapolates to ~205 MiB, the low end of the 200–400 MiB guessed above.
>
> **So the AO-alone baseline for the trigger below is 492 quads per drawn chunk at r4h/2v, and
> M5c must stay under ~984.**

### Tests

- **Merge exactness, and it is the sharp one.** For every emitted quad, re-derive the AO of
  every voxel face it covers from the padded chunk, and assert all of them share the quad's
  four-tuple. A merge that averaged a corner away fails this and nothing else would catch it.
- **A solid chunk in open air still emits 6 quads.** Every corner is fully exposed, so AO is
  constant and merging must be unaffected. This is the regression test for "AO gratuitously
  breaks flat surfaces".
- **Hand-built shapes**: an inside corner reads 0 at the shared vertex; a lone protruding cube
  reads 3 everywhere; a face against a flat plane reads 2 along the contact edge.
- **The flip is symmetric**: a quad and its mirror image produce mirror-image geometry.
- **Break-recipes to run and watch fail**: swap `side1`/`side2`; drop the `corner` term; drop the
  `side1 && side2` case; remove `CornerShade` from the merge key; force the flip off. The one
  that comes back green is a hole, a documentation correction, or unreachable at these
  parameters — and this project has hit all three.

> **Run 2026-08-30. One came back green and it is the third category.** Swapping `side1` and
> `side2` is a **no-op**: `(s1 && s2) ? 0 : 3 − (s1 + s2 + c)` is symmetric in its two sides,
> because both `&&` and `+` commute, so the mutation cannot change any output. The meaningful
> version is mislabelling *which corner* gets which sample — swap the corner sign table — and 8
> tests catch that. The others: drop `corner` 4, drop the `side1 && side2` case 5, sample the
> diagonal on one axis 6, remove `CornerShade` from the key 8, force the flip off 6, wind the
> flipped triangles backwards 2.
>
> **"A face against a flat plane reads 2 along the contact edge" reads 1.** A continuous plane
> fills the diagonal corner voxel as well as the side one. Two is what a *lone* side voxel with
> an open corner gives, and both rows are now asserted.

---

## M5b — The light grid, propagation, and the un-light pass

> **Built 2026-08-30.** Four things below were settled differently by contact with the code and
> are annotated inline: the storage budget, the oracle, what `Invalidate` has to touch, and which
> test shape can actually catch the skylight rule. Suite 496 → 518.

### Storage

A `ChunkLight`: 8×8×8 = **512 cells per chunk**, one byte each, 4 bits skylight and 4 bits
block light. **512 bytes per chunk**, against ~2 KB of voxel storage and ~46 KiB of mesh. Light
is not where the memory goes.

Indexed `[z][x][y]`, Y contiguous, same as everything else — and for the same reason, harder
here than anywhere: skylight propagates *straight down*, so the inner loop of the most common
propagation in the game walks Y. This is the 2026-08-25 lesson, and it applies more to lighting
than to the thing that taught it.

`LightCellSize` is a constant with the same constraint `CaveLatticeStep` has: **a power of two
dividing 32**, so a chunk boundary is always a cell boundary and independently lit chunks agree.
The constructor throws otherwise — that constraint is for correctness and is not optional.

**1 m is settled (2026-08-30): DESIGN's number stands.** The `--lightcell` A/B flag is therefore
**not built**, on the same rule as every other named fallback here — it exists to answer a
question, and the question has been answered. **Revisit trigger:** M5d plays a `hand_pick` tunnel
and the light reads as blobby across a bore narrower than one cell. Then 0.5 m costs 4 KB per
chunk against 512 bytes and 8× the propagation, the constant already permits it, and the flag is
worth building to compare.

### Cell conductance, which is the problem the coarse grid actually has

**A 1 m cell holds 64 voxels, and `hand_pick`'s bore is 0.75 m.** The tunnels this game is about
are *narrower than one light cell*. So "is this cell opaque" has no good binary answer:

- *Any opaque voxel → cell opaque*: a tunnel never conducts light, and torches light nothing.
- *Any air voxel → cell open*: a 0.25 m wall — which is a **normal wall thickness here** — leaks
  light between a lit chamber and a dark one.

So opacity is not a property of the cell. It is a property of the **face between two cells**:

> For each cell and each of its 6 neighbours, light crosses iff some voxel pair across the shared
> 4×4 voxel boundary is non-opaque on both sides.

> **Corrected 2026-08-30: the derived side is 1.5 KB per chunk, not 512 bytes.** Conductance is
> six bits as planned, but the un-light walk also needs each cell's **intrinsic source** — the
> sky-source bit and the emission nibble — or it erases the lamp it is walking away from and only
> a from-scratch flood could put it back. Eleven bits, stored as a `ushort`. Still nothing
> against ~46 KiB of mesh.

Six bits per cell, another 512 bytes per chunk, derived from the voxels at light-build time from
the same padded copy meshing already takes. A 0.75 m tunnel conducts along its axis; a 0.25 m
wall does not. 16 pair tests × 6 faces × 512 cells = 49,152 tests per chunk, comparable to one
mesher sweep — and it is computed once per chunk, not once per relight.

**The honest cost, recorded rather than discovered:** the *value* is still per cell, so rock
within 1 m of a lit tunnel samples that tunnel's light and glows slightly. Sampling half a cell
along the face normal hides most of it. If it reads badly, the lever is `--lightcell=2`, which
costs 8× of 512 bytes and nothing else.

### Skylight, and the thing the vertical radius breaks

Every voxel game that propagates skylight from the top of the world loads full vertical columns.
**This one does not** — the vertical radius is 2–3 chunks, 16–24 m, and that is a measured,
locked decision. So there is no "top of the world" to flood down from.

The seed is analytic instead. `WorldGenerator.SurfaceHeightAt(x, z)` already exists, is already
pure, and already gives the topmost solid voxel of an unedited column. A cell above it is full
skylight by construction, no propagation required, no chunks above needed.

Which is right until the player digs, and digging is the entire game. So one more structure:

**`SkyColumn`** — per chunk *column* `(cx, cz)`, a 32×32 map of "the Y above which this column is
open to the sky", seeded from `SurfaceHeightAt` and lowered when an edit removes the topmost
opaque voxel. ~2 KB per column as `short`; at r6h that is 169 columns, **338 KB**. Refcounted on
the *horizontal* footprint, so it survives the vertical unloading that happens constantly as the
player descends.

Its lifetime deliberately matches the edited chunks': lost on horizontal unload today, persisted
at M6. That is consistent with M3's already-recorded limitation that an edited chunk which
unloads loses its edits — a shaft whose chunks were regenerated from seed has no shaft to light.

**This is also exactly the structure M7 needs** for *"kill particles where skylight is occluded,
so rain stops at the mine entrance"*. It is being built one milestone early because skylight
cannot be correct without it, not because M7 asked.

**Confirmed for M5b 2026-08-30, and split across two parts rather than one.** The structure and
its rules — seed from the heightmap, lower on an edit, query a column — are a pure function and
belong in M5b with the rest of the fuzzable half. Its **lifetime** is not: a horizontal refcount
that survives vertical unload is streaming state, and it goes in M5c with the other streaming
coupling. Keeping them together would put the one mutable, lifetime-bound thing in M5 inside the
part whose whole value is that it is a pure function with a sharp oracle.

Two consequences of building it here rather than at M7:

- **It is a second refcount, on a different axis and a different object.** M2c recorded "don't
  add a second refcount to fix the margin" and this is not that — it is a column-lifetime
  problem that the chunk refcount cannot express, because the whole point is surviving the
  vertical unload the chunk refcount performs. Worth saying out loud so the two are not confused
  later.
- **The oracle has to reach it.** The incremental-equals-from-scratch fuzz must include edits
  that break through a column's topmost opaque voxel, or the sky-height path is never exercised
  and the comparison passes for the wrong reason.

Skylight's propagation rule is the standard pair: **full value straight down through open cells
with no attenuation**, −1 per metre horizontally. The no-attenuation-downward rule is what makes
a shaft a shaft rather than a 15 m-deep glow.

> **One word added 2026-08-30: at *full* brightness.** The free fall applies only to a value of
> 15. An already-attenuated value falling for free would hang a lit column under every dim patch
> in the world — a tunnel lit to 9 painting 9 straight down to bedrock.

### Block light, and the fact that nothing emits yet

`MaterialDefinition` has no emission field, and **nothing in the game can place a light source
until M6's placement exists.** So M5b adds:

- `MaterialDefinition.LightEmission` (0..15), projected into `MaterialTable` alongside
  `Hardness` — same shape as M3c's projection, same reason.
- One emissive material in `mods/minercraft/materials/`, because the base game is a mod and a
  light source is content, not a constant.
- A debug bind that calls the existing `SetVoxel` to place it. **One key, one material, no
  inventory** — deliberately the smallest thing that makes M5d playable, and it is not M6's
  placement wearing a hat.

`ShippedContentTests` gains the corresponding numeric check: some shipped material emits, and its
emission is inside 1..15, because the format having a field is not the same as the content using
it meaningfully.

### The un-light pass, which TODO already flags as the fiddly one

Adding light is a BFS that only ever overwrites a lower value. Removing it is the two-queue
algorithm:

1. Walk out from the removed source, zeroing every cell whose value is exactly one less than the
   cell it came from — those are the cells this source lit and nothing else did.
2. Any neighbour with a value ≥ the propagating one was lit by something else. Collect it.
3. Run the add-BFS from the collected set.

Miss step 2 and light does not come back where two sources overlapped. That is the dark patch
this milestone exists to avoid, and it is invisible until somebody breaks a wall in exactly the
wrong place.

### The oracle, and it is the best one available in this milestone

> **After any sequence of edits, the incrementally maintained grid must equal a from-scratch
> propagation, cell for cell.**

> **Bettered 2026-08-30, and the difference is a category rather than a degree.** The above runs
> the *same add pass* as the subject, so the only thing it can independently catch is the
> un-light pass. The propagation invariant
> `V = max(S, max over conducting neighbours (V(n) − attenuation))` has a **unique** fixpoint — a
> cycle of equal values would need zero attenuation all the way round, and the only such step is
> full skylight falling downward, which strictly decreases Y. So it can be checked **locally,
> cell by cell, with no propagation in the check at all**, which is M4a's sealed room rather than
> an agreement between two implementations. Both are asserted, plus a third that fell out free:
> the incrementally maintained sky heights must equal a fresh downward scan of the voxels.

No reference implementation to get wrong — from-scratch propagation *is* the definition, it is
just too slow to run every frame. No tolerance, no sampling: it is an array comparison. This is
the same category as M4a's sealed room, and it is the reason M5b is genuinely tractable despite
being the algorithmically fiddliest thing in the project so far.

**Watch it fail.** Removing step 2 above should break it within a handful of edits. If it does
not, the fuzz is not placing overlapping sources and needs to be told to.

### Other tests

- **Analytic falloff.** A single source in an empty world: light at Manhattan distance *d* is
  exactly `15 − d`. A 6-connected BFS has no freedom here, so this is an equality test.
- **A 0.25 m wall blocks; a 0.75 m tunnel conducts.** The two tests that fail for the two wrong
  conductance rules, one each.
- **An open column is full skylight to the surface and dark one cell below it.**
- **Determinism**, bitwise, across two runs — the multiplayer claim, same as M4a's.
- **Break-recipes**: drop the "only if greater" guard in the add pass (over-lights, may not
  terminate); zero unconditionally in the removal pass (fails the oracle); attenuate skylight
  going down (a shaft dies at 15 m); make conductance symmetric-by-assumption rather than
  derived per face.

> **Run 2026-08-30: twelve recipes, ten bit, and the two that did not were different problems.**
>
> - **The six-neighbour expansion in `Invalidate` was provably dead work.** Written for the
>   closing-face case — a walk from A never examines B once the face stops conducting — but
>   conductance is recorded on **both** cells of a face, so a changed face always reports both in
>   `BuildDerived`'s changed set. Removing it fails nothing and cannot. Deleted, the caller's
>   contract documented, and the property pinned by `SealingAFaceReportsBothOfItsCells`; breaking
>   the *reporting* instead fails 11 tests.
> - **"Any skylight value falls unattenuated" was a genuine hole**, because a shaft straight down
>   from the surface carries *full* skylight, which falls free under both rules. The shape that
>   separates them is a branch off the shaft with a **second shaft dropping from its far end**,
>   fed at 10 rather than 15.
> - **The "only if greater" recipe hangs rather than failing**, exactly as predicted above. A
>   battery has to treat a timeout as its own outcome or it reads as infrastructure trouble.
>
> **And the plan's "no attenuation straight down" needs one word added: at full brightness.**
> Letting an already-attenuated value fall for free hangs a lit column under every dim patch in
> the world.

---

## M5c — The pipeline: the stage, the sample, and the relight

### The problem

Fitting a not-per-chunk algorithm into a per-chunk pipeline, without either stalling the barrier
or drawing a chunk whose light has not arrived.

### A stage, not a state

`ChunkState` is the job-pipeline enum — `Queued → Generating → Generated → Meshing → Meshed` —
and light is not a job. `GenerationStage` is the barrier enum, and light *is* a barrier: a chunk
may mesh once its neighbours' light exists.

So light is a third `GenerationStage`, after `Features`, and `ChunkStreamer.CanMesh` gains one
clause. Nothing about `ChunkState` changes, which is the point — no new job kind, no new
transition, no new way for a chunk to get stuck mid-pipeline.

**The one thing that is easy to get wrong here** is the same thing M2c-2 recorded: advancing one
chunk must push its 26 neighbours. Light reaching a chunk may be the last thing its whole
neighbourhood was waiting on, and missing that push stalls the pipeline silently with everything
idle and nothing obviously wrong.

#### What sets the stage — settled 2026-08-30

The paragraph above says light is a barrier and stops there, and the thing it skips is that **a
global BFS has no per-chunk completion event**. `Features` is set by the sim thread finishing a
call for that chunk; there is no equivalent moment for light, because the queue is one queue over
the whole loaded region and a frontier crosses chunk boundaries without noticing them.

A chunk's light is final when two separate things hold, and only one of them is per chunk:

- **Structural** — every chunk whose light can reach it has derived data. Light reaches 15 cells,
  just under two chunks, so that is the chunks within two. Inside the visible box the load margin
  already guarantees it; this is the *third* load-bearing reason for the margin cashed in.
- **Temporal** — the propagation has actually run. That one is global, because the queue is.

So: **a chunk advances to `Lit` when its derived data is built and the propagator has no work
outstanding.** A per-chunk stage whose condition carries one global term.

**Rejected: a per-chunk count of how many of its cells are queued.** It looks like the way to
make the temporal half local, and it is wrong in the direction that does not show up in testing —
a chunk with no queued cells of its own can still have a neighbour's frontier one step from
spilling into it, so the count reads settled while the light is still moving.

**The global term is not the stall it looks like**, and the arithmetic is already in this
document: the initial fill at r4h/2v is ~779k cell visits, estimated at ~15 ms of work, which
under a 1 ms frame budget is ~15 frames — a quarter of a second, against the ~0.22 s the spawn
gate costs anyway. In flight a new chunk column is a few chunks of light; a mining swing is a
handful of cells, and most swings change none at all. **M5d measures the first of those; all
three are estimates.**

**What makes the coarseness safe is that being wrong is recoverable by the path that already has
to exist.** If light changes in an already-meshed chunk — a chunk loads two away, or the player
breaks through — the drain reports which cells moved and every chunk whose padded grid holds one
is marked for a remesh. That is the same mechanism as the edit path below, so streaming and
mining share it with no case analysis, and neither is a special case of the other.

### The padded light grid

Trilinear sampling at a vertex needs the eight cell *centres* around it. A vertex at the chunk's
far corner sits at cell coordinate 8.0; the interior centres run 0.5…7.5; so it needs cells 7
and 8, and cell 8 belongs to the neighbour. A vertex at 0.0 needs cells −1 and 0.

**10³ = 1,000 bytes, covering −1…8** — exactly the shape `PaddedChunk` has for voxels, gathered
from the same 27 references the mesh job already captures.

#### It is gathered on the sim thread, unlike the voxel copy — settled 2026-08-30

The sentence that used to close this section said `MeshChunkJob` takes a second neighbourhood
array and nothing else changes. **It cannot: a `ChunkLight` reference is not safe to hand a
worker.**

Holding 27 `VoxelChunk` references is safe for one specific reason, and it is M3a's rather than
M2a's — **voxels are copy-on-write.** A chunk handed to a job is immutable from then on, and an
edit that finds the flag set clones instead of writing. Light has no such rule and cannot cheaply
have one: the propagator writes into these 512-byte arrays continuously and in place, including
while a mesh job is running, and giving light copy-on-write would mean a clone every time a drain
touched a chunk — thousands per drain — against **one 1,000-byte copy per mesh job**.

So the padded light grid is built at submission, on the sim thread, and the job carries a value
instead of references. 1,000 bytes against the 157 KB padded voxel copy the worker still builds
for itself: **0.6%**, and it buys the race away entirely.

**Recorded because it reads as an inconsistency with `PaddedChunk` and invites being "fixed".**
The two are built in different places for reasons that do not transfer in either direction.

### Re-light on edit, and the number that decides M5's fallback

An edit changes conductance, and possibly an emitter. Run the un-light/re-light pass, collect the
set of cells whose value actually changed, and mark for remesh every chunk whose padded light
grid contains one — which is the owning chunk plus, for a cell on a boundary, its neighbour.

**Marking only the owning chunk is the break-recipe, and the symptom is a light seam on the chunk
grid.** M3a's `Neighbourhood()` marks all 27 for exactly the analogous reason.

The scope, estimated:

| Edit | Cells changed | Chunks to remesh |
|---|---|---|
| A swing in solid rock | 0 — no cell face opens | 0 |
| A swing opening a tunnel one cell further | a handful along the bore | 1–2 |
| A light placed in open air | a 15 m radius | up to 5×5×5 = **125** |
| A light placed in a tunnel | along the tunnel only | far fewer |

**The first row is the quiet dividend of the coarse grid**: most swings change no light at all,
because a 0.4 m sphere inside a 1 m cell does not open a cell face. Mining is not a relighting
storm; breaking *through* is.

The last row is the one to fear, and it is a one-off — a torch is placed once. The number that
decides the fragment-shader fallback is not the torch, it is **breaking into a lit space while
mining at five swings a second**. Predict it, then measure it, and if it exceeds the trigger
recorded above, move light out of the vertex.

**The cheap mitigation before the expensive one:** a light-only remesh does not need to be
urgent. A relight remesh can be ordinary priority, and light that is 100 ms stale is invisible.

#### It is two flags, not one line — settled 2026-08-30

This was written as "`SubmitMesh` already distinguishes an edit remesh (band3) from a streaming
one, so it is one line". **It does not.** `StreamedChunk.NeedsRemesh` carries both meanings at
once — *the mesh is stale* and *the player is waiting on it* — and `SubmitMesh` reads the single
flag for both, which is exactly right while an edit is the only thing that can invalidate a mesh
and stops being right here.

So `NeedsRemesh` keeps the first meaning and a new `RemeshUrgent` carries the second. An edit
sets both, a relight sets only the first, and `SubmitMesh` clears both.

**And a relight only marks a chunk that has actually drawn something.** A chunk that has never
meshed needs no marking: the barrier already holds it until its light exists, and marking it
would make its *first* mesh an urgent one. During the initial fill every chunk's light changes,
so that inverts the whole priority ordering — the distance-ordered streaming queue would arrive
uniformly at band3 and the ordering that M2a exists to provide would be silently gone. Nothing
would look broken; the world would just load in the wrong order.

### The shader and the sun

**Settled 2026-08-30: the directional light goes. The grid is the light.**

TODO carried this as an open item — *"Ambient fill is low — faces angled away from the sun go
nearly black. M5 lighting replaces this shading path entirely"* — and M5c is where it comes due.
The current look is Godot's directional light and ambient, modulated by a constant `shade`; after
M5c there is no directional light and no Lambert term, and `Voxel.gdshader` renders `unshaded`.

```
ALBEDO = texture(...) * faceTint(NORMAL) * (shadeSky * skyTint * skyBrightness
                                          + shadeBlock * blockTint)
```

Three notes on that line, each of which is a decision:

- **The face tint is computed in the shader from `NORMAL`, not baked into the vertex.** Top
  brightest, sides middling, bottom darkest — the cheap fake for directional shading that every
  voxel game uses. Keeping it in the shader keeps a *look* constant that will certainly be tuned
  out of the merge key, and it costs nothing, because a quad has exactly one normal.
- **`skyBrightness` is a uniform and there is no day/night cycle behind it yet.** It is here
  because the alternative — scaling the skylight seed and reflooding — relights the world every
  frame, and knowing that now is what stops someone implementing it that way later.
- **This retires two long-standing open items at once**: the low ambient fill, and the bright and
  dark slivers M1's screenshots recorded following terrain contours. Those are the sun hitting
  the integer heightmap's risers. With no sun there are no slivers, and the terracing goes back
  to being geometry rather than a lighting artefact.

What remains a *look* question for M5d is whether it reads better, not whether it happens.

### Tests

- **The barrier**: a chunk does not mesh until its 26 neighbours are lit. The margin chunk fails
  it by construction, as it does today.
- **Exact remesh scope**: an edit marks every chunk whose sampled light changed and no others.
  Both directions asserted — over-marking is a performance bug that hides correctness bugs.
- **Unload and revive**: a chunk releases its light grid; a chunk that comes back gets it
  recomputed rather than inheriting a stale one.
- **An edit landing mid-mesh** relights and re-marks, the same way `NeedsRemesh` survives a mesh
  completing.
- **The no-chunk-left-waiting fuzz, extended.** It exists, it runs against a real scheduler on
  real threads, and a light stage is exactly the kind of addition that can leave a chunk waiting
  on something that already happened.

---

## M5d — Feel and measurement

### The question this milestone is really asking

**Is a 1 m light grid enough resolution for a 0.25 m world, and does AO make voxels read as
sculpted rather than as cubes?**

Both are look questions, both are unanswerable headlessly, and one of them is a DESIGN-locked
number that this is the first opportunity to test against a screen rather than an argument.

### The half that gets measured

- **Quads per chunk**, AO off / AO on / AO+light on. Three numbers, one flag each, and they are
  the whole of the fragment-shader decision.
- **Mesh memory**, which follows quads and is the M2d finding this milestone most disturbs.
- **Light propagation cost per frame**, timed directly. Cells visited per second, with the
  denominator printed beside it.
- **Remeshes per swing**, mining normally and mining through into a lit space, at five swings a
  second — against M3d's control of 1.04% of one core.
- **Initial fill time** from a standing start, against the estimated ~0.25 s.

**Time it directly. Do not read `frame ms`.** The headless frame-time trap has been walked into
four times — M2d-1, the mesh-apply budget, M3d, and M4d — and each time the directly-timed
number moved by a tenth of a millisecond while `frame ms` reported a spike that did not
reproduce. There is no reason to expect a fifth encounter to look different, and every reason to
expect one.

### The half that has to be played

- **Does a mine read as a mine?** This is the first milestone whose output is darkness, and
  darkness is the mechanic M13 eventually turns into a hazard.
- **Is 1 m too coarse?** Specifically: walking down a `hand_pick` tunnel, whose bore is narrower
  than one light cell. 1 m is settled and the A/B flag is deliberately not built — this is the
  play that would reopen it, and 0.5 m costs 4 KB per chunk if it does.
- **Does rock beside a lit tunnel glow?** The known cost of a per-cell value, half-mitigated by
  the normal offset.
- **Does AO earn its quads?** DESIGN calls it *"the single biggest visual payoff"*; that is a
  claim, and M5d is where a person confirms or refuses it.
- **Did the sun going away improve or flatten the look?** Settled that it goes; unsettled
  whether the face-tint constants that replace it are right.
- **Can you mine by torchlight** — which is the first time this game has asked the player to
  bring light with them, and TODO already records the current state as *"walking into your own
  tunnel is walking into the dark"*.

---

## Order and rationale

**M5a → M5b → M5c → M5d.**

**M5a first, and not lighting first**, despite DESIGN listing them the other way round. AO is
derived from voxel geometry alone; it needs nothing from the light grid; the padded chunk was
built to feed it; and it forces the one structurally expensive edit in this milestone — the
merge key. Doing it alone means the quad-count measurement has **exactly one cause**, and that
measurement is half of the biggest decision M5 makes. It is also the most visible change in the
milestone, which is worth landing early.

M5b next because it is a pure function with the sharpest oracle available, and it can be
finished and fuzzed before anything in the pipeline moves. M5c wants both of the others done so
that a bug in it is unambiguously an integration bug. M5d last, because look cannot be judged
before there is something to look at.

---

## Decisions, settled 2026-08-30

All four were settled before any code was written, which is the point of planning them. Each
carries the trigger that would reopen it.

| | Settled | Revisit trigger |
|---|---|---|
| **Light cell size** | **1 m.** DESIGN's number stands | M5d plays a `hand_pick` tunnel and the light reads blobby across a bore narrower than one cell. 0.5 m then costs 4 KB per chunk and 8× the propagation |
| **`SkyColumn`** | **M5b**, split: rules in M5b, lifetime in M5c | — |
| **Directional light** | **Gone.** The grid is the light | — |
| **Emission range** | **0..15**, matching the field with no scaling | *"Unless we come up with better lighting later"* — a wider field, or coloured light, would take the range with it |

The cell-size decision keeps DESIGN's rationale intact, so nothing in DESIGN changes for it. The
one worth restating is why it was a question at all: DESIGN reasons in **metres** — *"light is
low-frequency, nobody sees the difference, 64× cheaper"* — and the number that decides whether it
looks right is in **voxels**. A 1 m cell is 4×4×4 = 64 voxels, and the tunnels this game is about
are 0.75–1.25 m across, so the cell is wider than the corridor. That is the thing M5d is looking
for, and it is why the trigger is a play rather than a number.

**The emission range is a game-balance number wearing a storage costume** — 0..15 means the
brightest lamp any mod can define reaches exactly 15 m, and it reaches it because of a storage
decision rather than a design one. The same shape as terminal velocity being a streaming
parameter in a physics costume. Worth a mod author knowing before they build against it.

**One thing fell out of settling the directional light**, and it changed M5a rather than M5c: with
no sun, the grid is the only light, and DESIGN's M7 wet-surface term needs **skylight on its own**
in the fragment shader. So the vertex carries two channels rather than one pre-multiplied value.
See M5a above — it is the only place a settled decision moved work earlier rather than removing
it.

### Three more, settled before M5c's code

Found by reading M5b's `LightTestWorld` next to `ChunkPipeline`, which is the pairing M5b built
the fixture for. Each is written up in full in the M5c section above; this is the index.

| | Settled | Why it was not obvious |
|---|---|---|
| **What sets `Lit`** | Derived data built **and** the propagator drained. A per-chunk stage with one global term | A global BFS has no per-chunk completion event, and the local-looking substitute — counting a chunk's queued cells — reads settled while a neighbour's frontier is one step away |
| **Where the padded light grid is built** | **Sim thread, at submission.** 1,000 bytes copied, not 27 references | The voxel copy goes on the worker because voxels are copy-on-write since M3a. Light is not, and giving it copy-on-write costs thousands of clones per drain against one 1 KB copy per job |
| **Remesh urgency** | Two flags. `NeedsRemesh` is *stale*, `RemeshUrgent` is *the player is waiting* | One flag carried both meanings, which was right while an edit was the only way to invalidate a mesh. A relight is the second way |

**None of the three is a reversal** — they are places the plan stopped one step short, and the
step it stopped short of had a wrong answer sitting in plain sight. That is the shape M5a's
UV2 packing had too: the plan stated a preference, and the reason it was impossible was one
level below where the plan stopped looking.

---

## Deliberately not in M5

- **Coloured light.** Three channels instead of one, three BFS passes, and 4 bits each. Nothing
  in DESIGN asks for it, and the merge key would carry 12 bits per corner instead of 4.
- **Directional or cone lights.** A headlamp is a shader effect, not a propagated field, and it
  would be M10's machine content anyway.
- **Light attenuation through translucent materials.** Water is the only candidate and it does
  not exist until M8. The conductance derivation is the right place for it and the predicate is
  already shaped to take it.
- **Placement of light sources through a real UI.** M6. M5's debug bind is one key and one
  material, and it should stay embarrassing enough that nobody mistakes it for the feature.
- **Persisting light.** It is derived data — 512 bytes per chunk recomputed in microseconds
  against a chunk blob that costs kilobytes. M6 persists *edits*, and the light follows.
  `SkyColumn` is the exception and is genuinely edit-derived, so it is on M6's list.
- **Distance LOD interacting with lighting.** M13, and the coarse grid is already most of the
  answer — a 1 m light field downsamples where a voxel field does not.
- **Darkness as a hazard.** M13 lists it. M5 supplies the darkness and nothing that punishes it.
- **Baking AO into the light grid** rather than computing it per vertex. They are different
  frequencies — AO is a 0.25 m effect and the grid is 1 m — and merging them loses exactly the
  detail AO exists to add.
