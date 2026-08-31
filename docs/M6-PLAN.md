# M6 — v0.1 playable sandbox

> Planning doc. See [DESIGN.md](DESIGN.md) for the decisions this implements.
> Written 2026-08-31.

Every chunk in this world so far has been recoverable from nothing. Worldgen is a pure function
of seed and position; light is derived in microseconds; a mesh is thrown away and rebuilt as a
matter of routine. Losing any of it costs a few milliseconds. That is why `Forget` can delete a
chunk record inside a tight sweep over a shell of a thousand chunks and nothing anywhere has to
think about it.

**M6 is the first milestone that creates state whose only copy is on disk**, and the consequence
is not that saving is hard — it is that *unloading* stops being free:

> **`ChunkStreamer.Forget` becomes a write.** Today it is a dictionary removal on a chunk that
> the seed can reproduce exactly. From M6 it is the moment the player's work either reaches
> disk or is gone, and it is called synchronously, on the sim thread, from inside
> `ChunkBox.ForEachDifference` — which at r16 hands it a whole 37×37 shell in one frame.

That inverts the failure model the project has been built under. A meshing bug shows on screen.
A physics bug is felt within a second. A persistence bug destroys work that cannot be recreated,
usually shows up a session later, and by then the evidence is gone. This is the first milestone
where **the tests are the only thing standing between a defect and permanent data loss**, and
that is what the split below is organised around.

There is a second structural change, smaller but sharper:

> **A chunk gains a third possible source of voxels.** It was memory, falling back to the seed.
> It becomes memory, then disk, then the seed — and the *only* failure that matters is
> consulting them in the wrong order, because regenerating a chunk that had a blob is a silent,
> total loss of everything in it.

| Part | What it is | The risk it removes |
|---|---|---|
| **M6a** The blob and the world file | `VoxelChunk` ⇄ bytes, `SkyColumn` ⇄ bytes, the ID table, a format version, validation of what comes back | A format that has to change *after* there are saved worlds to migrate, which is the one mistake here with no cheap fix |
| **M6b** The store, the lifetime, the load path | `IChunkStore`, SQLite behind it, save on unload, load before generate, the saved-chunk index, the world open/close sequence | Losing an edit to a race — a revived chunk, a cancelled job, a shutdown with work still queued |
| **M6c** Placement and volumetric inventory | `Place.Cube/Slab/Ramp/Sphere` in Core, litres, the write path, the player-overlap rule | A brush that writes voxels by a route that skips the sky column, the relight queue, the border rule and `HasEdits` — four things at once, all silent |
| **M6d** Selection, feel and measurement | Hotbar and material selection, the play, and what any of it cost | Building a sandbox nobody has tried to build anything in |

**M6b is where the surprises are**, and every one of them is a race the existing pipeline already
has a documented answer for. That is the good news and the trap together: the answers are all in
`ChunkPipeline` and `StreamedChunk` already, written for meshes, and each one has to be copied
across deliberately rather than rediscovered. Where a mesh race leaves stale geometry on screen,
the same race on the save path loses a tunnel.

---

## The three decisions that set the shape

### 1. The store is an interface in Core; SQLite lives in its own assembly

`MinerCraft.Core` has **no NuGet dependency at all** today — not one, in five milestones. That is
not an accident of scope; it is what makes non-negotiable 1's multiplayer seam more than a
slogan, because a dedicated server is `dotnet run` against Core and nothing else.
`Microsoft.Data.Sqlite` would be Core's first, and it carries native binaries (`SQLitePCLRaw`)
that have to be shipped beside the assembly and survive a Godot export with
`EnableDynamicLoading`.

So:

- **`IChunkStore` in Core**, with an in-memory implementation for tests.
- **`MinerCraft.Persistence`**, a new project referencing Core, holding the SQLite implementation
  and the package reference. Core does not reference it. `MinerCraft.Game` and any future server
  host wire the two together.

This is not a new pattern — it is exactly `IContentSource` / `DirectoryContentSource` /
`InMemoryContentSource`, built at M1 for the same reason and stated there in the same words:
*"so the loader can be tested without touching disk"*. M6 gets that property for free by copying
it.

The consequence worth stating up front: **`ChunkPipeline` takes an `IChunkStore?`, and null means
no persistence.** All 559 existing tests construct pipelines with no store and must keep passing
with no edit. A store that were mandatory would mean touching every one of them, and a test suite
edited in the same pass as the feature it guards is a test suite that has stopped guarding it.

**No ADR.** DESIGN already locks SQLite over region files, with the reasoning recorded; this is
where it lands, not whether. If the recommendation had been to put the package *in* Core, that
would have earned one.

### 2. Saving is a job on the serial lane — and it is the one job that must never be cancelled

M2a built the serial lane for this and said so: *"Disk work is marked serial and runs one at a
time, because it contends on the same file anyway... Nothing uses the serial lane until M6
persistence — the flag exists now because retrofitting it means touching every submission site."*
The flag is `_batch.Add((job, cancellation, false))` in `ChunkPipeline.Submit`, and it has been
`false` at every call site since it was written.

**The argument that put light on the sim thread does not transfer, and should not be inherited.**
Light went there because a BFS whose frontier crosses chunk boundaries has no per-chunk window
that is correct — a 3×3×3 snapshot silently misses a source 14 m away. A chunk blob is a pure
function of one chunk, which is precisely the shape `MeshChunkJob` already has. There is nothing
global about it, and `fsync` on the sim thread is the textbook frame hitch.

Two things follow, and both are exceptions to rules the pipeline otherwise applies everywhere:

- **A save job must set `VoxelsSharedWithJob`** on its chunk, exactly as `Neighbourhood()` does
  for all 27 of a mesh job's. Not because a chunk being saved is normally written to — it is
  being unloaded — but because it can be **revived and then mined while the serialiser is
  mid-copy**, and M3a records what that costs: not a stale voxel, but `VoxelChunk.Set` promoting
  the representation and swapping the backing arrays under a reader. A `NullReferenceException`
  on a worker thread, from a race that needs a player to turn around at the right moment.
- **`OnChunkUnviewed` must not cancel it, and `Dispose` must not drop it.** Every other job in
  this engine is cancelled the instant nobody wants its chunk, and that is right for work whose
  only product is something we can rebuild. A cancelled save is a deleted tunnel. `JobResult`'s
  contract says *"a job still queued at shutdown reports Dropped"* — so **quitting the game with
  saves queued loses them**, and the shutdown path has to drain the serial lane rather than
  signal the token. This is the single most likely way to ship a data-loss bug in M6 and it is
  one line of `Dispose`.

**The dirty flag is the `NeedsRemesh` problem again, and the third time this shape has appeared.**
`StreamedChunk.NeedsRemesh` survives a mesh completing, for a reason its own comment states: an
edit landing *while* the job runs produced a mesh built from a pre-edit snapshot. A save has the
identical structure with a worse outcome. So:

- **`HasEdits` keeps its current meaning — "this chunk differs from the seed" — and is never
  cleared.** It is already set by `SetVoxel` and read by nothing.
- **`DirtySinceSave` is the new flag: "this chunk differs from what is on disk".** Cleared at
  *submission*, because `SetVoxel` will set it again if an edit lands during the job; **restored
  by `StepBack` if the job is dropped or faults**, because a save that did not happen must not
  look like one that did.

Conflating the two loses exactly the last edits before an unload, which is the worst possible
subset to lose: the most recent work, and only sometimes.

### 3. The ID table lives in the same database as the chunks, in the same transaction

Non-negotiable 2 says content IDs are namespaced strings resolved through a table **the world**
owns. The machinery has existed since M1 and has never had a caller:

| Built at M1 | Called by |
|---|---|
| `Registry.ExportIdTable()` — *"The ID table to write into the world file"* | nothing |
| `Registry.SeedIdTable(table)` — *"Restores a world's saved ID assignment"* | nothing |
| `GameContent.Load(source, savedMaterialIds)` | `Main.cs`, passing `null` |

M6 is where a non-negotiable stops being a claim and becomes a test. Three things about it:

- **The obvious layout is wrong.** A JSON world file beside a SQLite chunk database is the first
  instinct and it is the one arrangement that can desynchronise: a crash between writing the
  blobs and writing the table leaves chunks full of numeric IDs whose meaning is a version
  behind. **Table and blobs in one database, and any write that allocates a new numeric ID
  commits in the same transaction as the blob that first used it.** That is the property SQLite
  is being paid for; DESIGN calls it *"the pragmatic one for a solo project"*, and atomicity is
  most of what makes it pragmatic.
- **Startup reorders.** Content currently loads in `Main.LoadContent()` with no world open. It
  must become: open world → read ID table → `Load(source, savedIds)` → `Freeze` → construct the
  streamer. `SeedIdTable` throws if anything has registered first, which is the right failure and
  a loud one.
- **Non-negotiable 3 needs an explicit guard.** `MaterialIds.AirNumericId` is 0 *always*, and on
  a seeded world that holds only because entry 0 of the saved table is `minercraft:air`. A world
  file where it is not — corrupt, hand-edited, or written by a bug — makes a constant the whole
  engine assumes into a lie. **Validate entry 0 on open and refuse the world**, rather than
  discovering it as air-coloured stone.

TODO already carries the related item from M3c: *"`MaterialTable` indexes raw, so a material ID
past its `Count` throws `IndexOutOfRangeException`... The fix is validating chunk contents at
load: M6."* A blob from a world whose mods have changed is exactly how an out-of-range ID
arrives, so that validation is M6a's and it belongs at the deserialiser rather than at the first
mesh.

---

## M6a — The blob and the world file

**Pure Core. No SQLite, no file, no pipeline.** Bytes in, bytes out, fuzzable in milliseconds.

### The format, and the one thing about it that cannot be fixed later

Everything else in M6 can be rewritten in an afternoon. A format cannot, once anybody has a
saved world — and the person who has one first is Matthew, mid-milestone. So:

- **A version byte first, and an unknown version is refused rather than read.** A format that
  can only be read is a format that silently misreads its successor.
- **A storage-kind byte**, mirroring `VoxelChunk.StorageKind`. Uniform is a handful of bytes and
  is common: dig a chunk out entirely and it is uniform air, which differs from a seed that says
  uniform stone.
- **The palette is written as numeric IDs**, which are world-stable by construction — that is
  what the ID table in the same database buys. `VoxelChunk`'s per-chunk palette is a compression
  device and unstable, and its own doc comment warns that conflating it with the registry's table
  is *"a nasty bug"*. Writing world IDs through it is the correct direction and the note is worth
  keeping visible in the serialiser.

### Deflate, because voxel data is the best case for it and it costs nothing to have

`System.IO.Compression` is in the BCL, so this is not a dependency. A palettised chunk at 4 bits
is 32,768 ÷ 16 = 2,048 words = **16,384 bytes** plus its palette; direct storage is 64 KB and
should essentially never occur. Voxel data is long runs of one index, which is what deflate is
best at.

**Estimate, labelled as one: 10–50× on this data, so ~1 KB for a typical edited chunk.** With a
few hundred edited chunks in a long session, a save file is a few hundred KB. **That arithmetic
is worth doing before building anything, because it says the save file is not going to be a
problem** — and the thing the plan should be nervous about is therefore somewhere else. See the
read side, below.

### `Compact()` gets its first caller ever, and nobody has costed it

`VoxelChunk.Compact` has existed since M1 with the comment *"**Never called from an edit**...
This runs on serialisation, or when something explicitly asks."* Nothing has ever called it. That
sentence is a **prediction, not a decision**, and M6 is where it either becomes true or gets
retired.

What it costs: a full 32,768-voxel scan through `GetAt`, a dictionary insert per distinct
material, and a full repack. **Estimate: ~0.13 ms per chunk**, comparable to a fair share of a
mesh job, on every save.

What it buys: dropping dead palette entries and narrowing the bit width. The realistic worst case
is a chunk that briefly held a 17th material and is stuck at 8 bits — 32 KB rather than 16 KB, so
**2× before compression and much less after**, since deflate eats the redundancy anyway.

**Measure it; do not assume it.** If compression makes it worthless it should be dropped and the
doc comment corrected, which is a documentation fix in the M4a-zeroable-epsilon family: a claim
that was never wrong, never checked, and one line from going stale.

### `SkyColumn` — the one derived structure that must persist, and why it is not a judgement call

Light is 512 bytes recomputed in microseconds and is not saved. A sky column is 32×32 `short` =
**2,048 bytes**, and the argument for saving it is not size:

> A column's open height is the topmost opaque voxel **across all Y**. Re-deriving it needs every
> chunk between the analytic surface and the current height. The vertical radius is 2–3 chunks,
> so **those chunks are not loaded**. That is the same reason `SkyColumn` exists at all — there
> is no top of the world to flood down from — and it is what makes the column the only piece of
> derived data here that genuinely cannot be re-derived.

The failure if it is skipped is concrete and is the classic voxel bug arriving by a new route: a
shaft reloads with its column seeded from `SurfaceHeightAt`, so `openAboveY` is the *pre-dig*
surface, every cell in the shaft fails `IsCellOpenToSky`, and **the shaft is dark for a reason
nothing on screen can explain**. Loading a chunk without its column is worse than loading
neither.

Two mechanical consequences:

- **A column needs its own dirty bit**, the mirror of `HasEdits`. `OnVoxelChanged` returning true
  is exactly the moment to set it, and it is exactly the right test — it returns true only when
  the height actually *moved*, so a column dug below its top is unchanged from its analytic seed
  and correctly not written.
- **The save hook is `ReleaseColumn`, not `Forget`.** Two save hooks on two different lifetimes,
  because a column is refcounted on (x, z) and survives the vertical unload the chunk refcount
  performs as the player descends. `ReleaseColumn`'s existing comment already names this:
  *"M6 persists both, and it is the same decision for both."*

### Tests

The oracle is **round-trip identity, voxel for voxel**, and its limits should be stated rather
than trusted:

- **Voxel-for-voxel, not representation-for-representation.** A chunk saved palettised may legally
  come back uniform if it was compacted, and that is correct. Asserting on `Kind` would fail on
  working code — the M3b shape, where the first oracle was wrong in the subject's favour.
- **A round trip runs the same writer and the same reader, so a symmetric bug passes.** A field
  written to the wrong offset and read from the wrong offset agrees with itself. The complement is
  a **hand-constructed blob asserted byte for byte**, one per storage kind — small, and it is the
  only thing that pins the format rather than the pair.
- **Fuzz across all three storage kinds**, including the promotion boundaries: a 2-material chunk
  at 1 bit, 17 materials at 8, and one past 256 forced to direct.
- **Refusal, not repair**: an unknown version byte, a truncated blob, a material ID past the
  table's `Count`, and an ID table whose entry 0 is not `minercraft:air`. All four must throw with
  the world named, because a world that half-loads is worse than one that will not open.
- **Watch each fail before trusting it** — the rule is four for four across M3, M5a and M5c-1, and
  the break-recipe battery here should include *writing the palette as per-chunk indices rather
  than world IDs*, which is the mistake the type system cannot prevent and which round-trips
  perfectly within one session.

---

## M6b — The store, the lifetime, and the load path

**This is the part with the races in it.** Everything below is a rule the pipeline already
enforces for meshes; the work is transferring each one across on purpose.

### The load path, and the index that keeps generation free

`OnChunkViewed` currently does `Unloaded → Queued`, and `Advance` submits a `GenerateChunkJob`.
The new question is "does this chunk have a blob?", and it must be answerable **without I/O**,
because it is asked on the sim thread once per chunk entering the load box — a 37×37 shell at
r16.

- **An in-memory index of saved chunk keys, read once at world open.** `SELECT cx, cy, cz FROM
  chunks` — 12 bytes plus set overhead per entry, so **~5 MB at 100,000 edited chunks**, which is
  a heavily played world. The decision is then a hash lookup.
- **Rejected: asking the database per chunk.** 1,521 point queries down one serial lane to fill
  r4h/2v, nearly all of which answer "no". The index turns the common answer into a hash miss.
- Revisit trigger: an index read at open that becomes noticeable, which the arithmetic puts at
  roughly a million saved chunks.

Order of consultation, and it is the whole correctness question: **memory, then the index, then
the seed.** A chunk regenerated because the index missed it is a total, silent loss.

### The number this plan is most likely to have got wrong, stated so it can be checked

Every milestone here has costed one half and been silent about the other; M5-PLAN costed
propagation well and said nothing about the derive, which was the binding constraint. The
candidate for M6 is the **read** side, because "persistence" reads as "saving" and the write path
is where attention goes.

Generation at r4h/2v: 1,521 chunks × 0.47 ms (measured, Release) ÷ 8 workers ≈ **89 ms of wall
clock** to fill the world. Loads replace generates one for one, but the serial lane runs them
**one at a time**:

| per-load cost | loads before the serial lane exceeds the generate path it replaced |
|---|---|
| 0.1 ms | ~890 of 1,521 |
| 0.3 ms | ~300 of 1,521 |

**So a world the player has dug into heavily can be slower to enter than one generated from
scratch**, and the crossover is well inside what a real base would produce. That is a genuinely
surprising direction and it is what to measure first at M6d.

**Named and deliberately not built: batched loads.** One job, one query, many chunks — SQLite's
per-statement overhead dominates a 16 KB blob read, so batching attacks the actual cost rather
than the lane. **Do not build it before the number says so**, on the standing rule; every named
fallback in this project so far has turned out unnecessary, one of them backwards. The
alternative lever — parallel reads, which WAL mode permits — is the *worse* one: it contends for
the same eight workers generation is already saturating, and it reopens a decision M2a made with
its reasons written down.

### The column restore, which cannot go where the column is created

`RetainColumn` runs inside `View`, inside `ForEachDifference`, on the sim thread. A one-chunk
horizontal step at r16 creates **~37 new columns**; at an estimated 20 µs per 2 KB read that is
**~0.74 ms in a single frame**, unamortised, against a main-thread streaming budget that M2d
measured plateauing near 3% of a core. That is a spike, not a cost.

**Recommendation: seed analytically as now, queue a column restore, and call
`QueueColumnLightBuild` when it lands.** The machinery exists — M5c-1 built
`QueueColumnLightBuild` for precisely "this column's sky heights moved". The price is a
one-or-two-frame window where a reloaded shaft is lit against the pre-dig surface, which is the
same category of transient as a chunk that has generated and not yet meshed.

The alternative is synchronous loading in `RetainColumn`, and the number above is why it is not
the recommendation rather than an assertion that it is wrong.

### The write path

- **Save on unload**, from `Forget`'s caller rather than `Forget` itself — the chunk must survive
  long enough to be serialised, and M2c-2's retention machinery already does exactly this:
  `OutstandingJobs > 0` retains a chunk past its last viewer, and `ForgetIfUnwanted` collects it
  when the job reports. **The async save needs no new lifetime concept.** It is already built and
  already fuzzed by the exactly-once invariant.
- **Plus a periodic flush of the dirty set**, because unload-only means a crash loses everything
  since the player last moved far enough — which for someone digging in one spot for an hour is
  everything. The dirty set after a large excavation is tens of chunks at ~1 KB, so this is
  cheap and the argument for it is purely crash safety.
- **Plus on quit**, synchronously, draining the lane. See decision 2.

### Tests

- **The oracle: edit → unload → reload → the world is voxel-identical.** Boolean, no reference
  implementation, the M4a-sealed-room family.
- **The mod-set round trip, which is non-negotiable 2's first real test in five milestones.** Save
  with a mod installed; remove it; reopen — the blocks must be placeholders retaining their
  identifiers, not reinterpreted as whatever now holds those numbers. Reinstall; reopen — the
  world is exactly as it was. `Registry` has promised this since M1 and nothing has ever asked it
  to deliver.
- **The races, each as its own deterministic test**, because none is reachable by ordinary play:
  a chunk revived mid-save and then edited; a save job dropped; a shutdown with saves queued; a
  chunk saved, unloaded, and re-viewed before the write completed.
- **A break-recipe on the call site, not only on the logic.** M5c-2's two real holes were both
  *joins* — replacing `GatherLight(...)` with `null` left 556 tests green. The equivalents here:
  never calling the store from `Forget`'s path at all, and clearing `DirtySinceSave` on completion
  rather than at submission. Both leave a green suite and lose data in play.

---

## M6c — Placement and volumetric inventory

### The write path is `SetVoxel`, and saying so is the entire point

M5c-1 recorded a revisit trigger: *"when `ApplyFeatures` starts placing anything it must route
through the relight path the way `SetVoxel` does."* **M6 does not fire that trigger** —
`ApplyFeatures` still places nothing, and placement is a different route. But M6 adds the
second-ever caller of the write path, and the risk is the same one:

A 1 m cube brush is 4×4×4 = **64 voxels**. Writing them straight into `chunk.Voxels` is faster,
obvious, and skips **four** things at once, all silently: the sky-column update, the light
rebuild queue, the eight-chunk border dirty rule, and `HasEdits` — which at M6 means the
placement is not saved. So: **`Place.*` calls `IVoxelEditor.SetVoxel` per voxel, exactly as
`Carve` does**, and the shared gate is where any future rule lands, the way `Carve.Take` holds
the hardness gate for both mining shapes.

`SetVoxel`'s "material is already there" early-out finally earns its keep. Its comment predicted
this: *"M6's placement is exactly the shape of caller that rewrites what is already there.
Deleting it because it no longer fires would be measuring one caller."* A cube brush against an
existing wall rewrites mostly-identical material, and each rewrite would otherwise clone a chunk
and dirty up to eight neighbours.

### The brushes are staircases, not slopes — and this is where the phrase misleads

DESIGN says *"1 m cube, slab, ramp, sphere"*, and "ramp" in a voxel game usually means a sloped
*block*. It cannot mean that here. M4d's deliberately-not list is explicit: *"Non-cube collision
shapes (slabs, ramps, ladders) — all 'a voxel is one material or another' violations wearing
different hats"*.

So a ramp brush is an **arrangement of 0.25 m cubes inside a 1 m cell**: a four-step staircase.
This is M3c's *"sub-voxel means the voxel is small, not that it is partly full"* restated for the
placement half, and it lands well — a 0.25 m rise is comfortably inside M4b's 0.5 m step height,
so a placed ramp is walkable by the physics that already exists, with no new collision shape and
no new anything. A slab is 4×2×4, 0.5 m thick, also walkable.

Brushes are **methods, not an `IShape` the caller supplies**, following `Carve`'s recorded
argument: there is no second implementer, and a ramp wants an orientation where a sphere wants a
radius, so the interface they would share is not yet knowable. A ramp's orientation comes from
the raycast's face normal plus the player's facing — **the first brush parameter that is not a
scalar**, and the first thing in this game that has a rotation at all.

### Where a brush lands: the 1 m lattice — recommended, and it is a real decision

`Hit.Previous` is one 0.25 m voxel; a brush is 64 of them. Two answers:

| | |
|---|---|
| **Snap to the world 1 m lattice** ← recommended | Brushes tile exactly. A wall built of cubes has no seams and no one-voxel gaps; the slab and ramp only make sense at all on a lattice, since a ramp that is not grid-aligned cannot be tiled into stairs. **The lattice is what makes the brush *set* coherent, not just the cube.** |
| Centre on the hit voxel | Feels more direct, and two adjacent placements overlap or leave a gap depending on where you pointed. |

DESIGN's *"fine detail comes from carving, not from placing"* is the argument: brushes are the
bulk tool, and bulk that tiles is the point. **This decision goes in Core**, on M3d's grounds —
*"where a swing lands is a game rule a multiplayer server would need"* — as the placement
counterpart to `Carve.SwingCentre`. Revisit trigger: **M6d plays it and lattice-snapping reads as
unresponsive.**

### Material appearing inside the player — recommended: refuse, and it retires a coupling

M4c recorded this as inherited: *"material appearing inside the box has no answer at M4 — M6's
placement can do it deliberately, M9's collapse by accident — and `PlayerSpawn.TryUnstick` is its
seed, including its up-only decision."*

**Recommendation: the brush skips voxels overlapping the player's AABB and places the rest**,
reporting them in their own bucket. Reasons:

- It is the least surprising behaviour, and *"you cannot build a block inside your own body"* is
  not a mechanic anyone will miss. Pillar-jumping still works, because mid-jump the cell below is
  not overlapping you.
- It needs no unstick in the common case, and **the unstick is still needed for M9's collapse**,
  which puts material inside the player by accident and has no alternative. So refusing here does
  not retire `TryUnstick` — it stops placement being a second, deliberate caller of a rule whose
  only honest home is "something went wrong".
- The partial-result shape already exists: `CarveResult`'s four buckets that must sum to
  `Candidates`, with `IsAccounted` asserted by every test *"because the failure it catches is a
  `continue` added later that is counted nowhere"*. A `PlaceResult` should be the same struct
  shape for the same reason, and refused-for-overlap is a bucket.

The alternative — place and shove the player up — is available and is one call. It is Matthew's
to overrule.

### Volumetric inventory, in litres

A 0.25 m voxel is 0.015625 m³ = **15.625 litres**. A 1 m brush cube is 64 voxels = **1,000 litres
= 1 m³**, which is the round number that makes the unit worth having. `CarveResult.Removed` is
already the number to convert, and it is already documented as *"the volume M6's inventory will
read, and air is not volume."*

Three decisions, and the first is the one that matters:

- **Unbounded at M6.** Bounding it now, with no containers, no conveyors and no crafting, means
  the player fills up and stops — a constraint with nothing on the other side of it. **The honest
  cost: an unbounded inventory means M6 cannot answer whether volume is a real constraint**, and
  DESIGN's claim that volumetric inventory *"makes big excavations meaningfully productive"* stays
  argued rather than played. Recorded revisit trigger: M10's containers or M11's crafting,
  whichever gives capacity somewhere to go.
- **Placement consumes it, and a short inventory places what it can** — another bucket, same
  struct.
- **It persists in the world file, keyed by numeric material ID**, which is safe for exactly the
  reason the blobs are: the ID table is in the same database. An inventory stored anywhere else
  has the identical mod-load-order failure and no fix. If the held *tool* is saved too, the tool
  registry needs seeding, and `GameContent.Tools` has already written down what that costs:
  *"it will be the same three lines."*

---

## M6d — Selection, feel and measurement

### The half that gets measured

| Question | Why it is the one to ask |
|---|---|
| **Time to enter a heavily-dug world, against generating the same box** | The crossover in M6b's table. The prediction is that loading can be *slower* than generating, which is the surprising direction and therefore the one to check first |
| What `Compact()` costs per save, and what it saves after deflate | Its only justification is a doc comment written five milestones before its first caller |
| Compression ratio, and total save size after a real session | Predicted ~1 KB per chunk and a few hundred KB per session. If that is wrong it is wrong by an order of magnitude, not by 20% |
| Sim-thread cost of the save burst when a shell unloads at speed | `Forget` became a write, and it runs inside a box-difference sweep |
| Placement cost per brush against a swing's measured 0.083–0.139 ms | 64 `SetVoxel` calls against a `hand_pick` sphere's ~17 |

**Use `update ms`, not `frame ms`.** The headless frame-time trap has now appeared four times
across M2d, M3d and M4d, and has been believed at least once each time.

### The half that has to be played

- **Dig a tunnel, fly away until it unloads, come back.** This is the milestone's whole reason for
  existing, it is the thing M5d-2's play kept running into, and it is a yes/no that no test
  produces.
- **Quit and reopen.** Same tunnel, same inventory, same place.
- **Build something.** Not "place a block" — a room, with walls, a floor and a ramp down into it.
  That is what says whether the 1 m lattice is right, whether the brush set is the right four
  shapes, and whether carving-for-detail-and-placing-for-bulk actually divides the way DESIGN
  claims.
- **Hotbar and material selection.** The number keys currently pick tool tiers, so material
  selection is a second axis and this is the project's first real UI. It is deliberately last:
  it is the part most likely to be redone once there is a reason to, and the least likely to be
  wrong in a way that costs anything.

---

## Order and rationale

**M6a → M6b → M6c → M6d.**

**Persistence before placement**, despite placement being the more enjoyable half and independent
of it. The reason is the milestone's own headline: play currently loses its results, so **every
hour of play before persistence lands is an hour thrown away — including the play that would
judge placement.** Placement built second is placement built on a world that keeps it, and M6c's
"go and build a room" test is only meaningful once the room survives being flown away from.

M6a before M6b for the reason M5b came before M5c: it is a pure function with a sharp oracle and
no threads, it can be fuzzed to exhaustion in milliseconds, and a bug found in M6b is then
unambiguously an integration bug. It is also the only part of M6 with a decision that cannot be
cheaply undone, and doing it first means undoing it is cheap for longest.

M6d last, because a sandbox cannot be judged before there is something to build in it.

---

## Decisions to settle before M6a's code

Each carries the trigger that would reopen it. Four are recommendations rather than settled, and
they are marked.

| | Recommended | Revisit trigger |
|---|---|---|
| **Where SQLite lives** | New `MinerCraft.Persistence` assembly; `IChunkStore` in Core; Core stays dependency-free | Core needing the store for a reason an interface cannot express |
| **Save as a job** | Serial lane, per M2a's built-and-unused flag. Not cancellable, drained at shutdown | — |
| **World file** | Same database as the chunks, same transaction as any ID it allocates | — |
| **Compression** | Deflate, BCL only, on the worker | Measured ratio under ~3×, at which point the CPU is not worth the opacity |
| **`Compact()` on save** | ← *decide by measuring at M6a*. Its only justification is an uncalled doc comment | — |
| **Load ordering** | memory → saved index → seed, with the index read once at open | ~10⁶ saved chunks |
| **Column restore** | ← *recommended*: analytic seed + queued restore + `QueueColumnLightBuild`, not synchronous I/O in `RetainColumn` | The one-or-two-frame wrong-sky-height window reading as a flicker |
| **Brush anchoring** | ← *recommended*: snap to the world 1 m lattice | M6d plays it and it reads as unresponsive |
| **Player overlap** | ← *recommended*: refuse the overlapping voxels, place the rest; `TryUnstick` stays M9's | Play wanting to build under itself in a way refusal blocks |
| **Inventory capacity** | ← *recommended*: unbounded at M6 | M10 containers or M11 crafting, whichever gives capacity somewhere to go |

**The one thing this plan has deliberately not costed, and says so:** how long a *human* tolerates
between pressing play and standing in their own base. Every number above is throughput. M5c-1
found that the metric that mattered was not throughput but *time to first draw*, and it found it
by sweeping a budget against when the world could first be seen rather than against how fast it
worked. The equivalent sweep here has no obvious knob to turn, which is exactly why it is the
thing to watch for.

---

## Deliberately not in M6

- **Region files.** DESIGN records the decision and its reasoning: they are the proper answer and
  SQLite is the pragmatic one. Nothing here reopens it.
- **Migration between format versions.** M6a refuses an unknown version; writing an upgrader
  before there is a second version is writing against a format that does not exist. The version
  byte is the whole of what M6 owes it.
- **`VACUUM`, save-file compaction, or a free list.** SQLite grows and reuses pages. Revisit when
  a save file's size is surprising.
- **Multiple worlds, a world-select screen, or naming.** One world at a fixed path. The moment
  there are two, the ID table's per-world ownership starts being *visible* rather than merely
  correct, and that is worth its own pass.
- **Persisting light.** Derived, and recomputed in microseconds. `SkyColumn` is the exception and
  it is M6a's, with the argument written out above.
- **Persisting the mesh.** Larger than the voxels it was built from (~46 KiB per drawn chunk
  against ~2 KB) and cheaper to rebuild than to read.
- **Dropped items.** DESIGN puts them here and is explicit that Godot physics is fine for them,
  but they are an entity system and M6 has no entities. They want the pass that introduces one.
- **Undo.** A sandbox invites it and it is a different data structure — a journal rather than a
  snapshot. Not free, not asked for, and cheaper to add on top of a working save than to build
  into one.
- **The entangled miner becoming real content.** Recorded at M5d-2 as wanting its own planning
  pass, because it is the tool whose defining property is unlimited reach and *reach is the
  player's, not the tool's* is locked twice. M6 does not touch it.
- **Bounded inventory, containers, or any crafting.** See M6c's recorded cost for what that
  defers.
