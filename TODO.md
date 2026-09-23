# Overburden — TODO

See [docs/DESIGN.md](docs/DESIGN.md) for the reasoning behind all of this.

**This file is the record — every task with the reasoning that produced it, newest work
deepest. It is not a list anybody can act from.** For that, see
[MATTHEW-TODO.md](MATTHEW-TODO.md): the play queue, the decisions waiting on Matthew's word and
the chores only he can run, kept to a screen or two. A play check written here gets a line
there when it is built; the answer comes back here.

## Decisions

- [x] **Engine — Godot 4 + C#.** Decided 2026-08-23. Real threads, renderer for free,
      4.7 mono already on disk.
- [x] **Voxel scale — 0.25 m.** Decided 2026-08-23.
- [x] **Chunk size — 32³ (8 m cube).** Decided 2026-08-23.
- [x] **No mobs.** Pressure comes from flooding, collapse and logistics.
- [x] **Mod support — data mods at M1, Lua at M12, no C# DLL loading.**
- [x] **Chunk storage order — Z-major, then X, then Y contiguous.** Revised 2026-08-25
      after reading godot_voxel, which uses the same `[z][x][y]` order for the same
      reason. The original 2026-08-24 choice (X contiguous) was wrong: its stated
      justification — "keeps a horizontal slab together for the lighting column pass" —
      was self-contradictory, and every hot path in this game is vertical (skylight,
      falling water, support propagation, groundwater drainage, columnar terrain gen).
- [x] **Build our own voxel engine, don't adopt godot_voxel.** Decided 2026-08-25, see
      [ADR-0001](docs/adr/ADR-0001-build-our-own-voxel-engine.md). Read theirs as
      reference; `reference/` is gitignored for exactly that.
      - [x] **Read godot_voxel's threaded mesh scheduling and chunk streaming before
            M2** — done 2026-08-26, and it paid for itself a second time. Findings in
            [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md).
      - [ ] Revisit trigger: M1c or M2 exceeding ~3 sessions each
      - [ ] Revisit trigger: M13 distance LOD, where their advantage is largest
- [x] **Mining granularity: sphere on the bare click, single voxel under Ctrl.** Raised and
      decided 2026-08-30; built the same day as `Carve.Chip` / `Carve.Voxel`, 9 tests, six
      break-recipes all caught. DESIGN's "mining is sculpting" line stands and now records that
      the alternative was considered and why it lost. The reasoning, kept: The capability is not in question — both exist and the modifier is the
      mechanism. **What is in question is which one a bare click does**, and it is an *economy*
      decision rather than a control one:
      - **It reverses a DESIGN line.** "Mining is sculpting: a pick swing removes a sphere or
        cone of material" — DESIGN.md, *Mining and placement — decoupled granularity*. Changing
        the default is a change to that, and DESIGN is where locked decisions live, so it wants
        a deliberate edit rather than a drift.
      - **The cost is ~17× excavation throughput with the starting pick.** `hand_pick`'s 0.4 m
        sphere is about 17 voxels; one voxel is one voxel. DESIGN's own argument for volumetric
        inventory is that it "makes big excavations meaningfully productive", and this game's
        premise is a *mining operation* fighting water and gravity. **The Taj Mahal case and the
        mining-operation case want opposite defaults.**
      - **Decided: the sphere is the bare click and the single voxel is the modifier.** Same
        two capabilities, same one modifier, no DESIGN reversal, and the productive action stays
        the default in the game about being productive. Precision is opt-in for the person who
        has decided to build something, which is the person who will happily hold a key.
      - **No conflict with volumetric inventory** — that one is safe either way. A voxel is
        0.25³ m³ = **15.625 litres**, so "one block at a time" is a *mining granularity* and
        litres-not-counts is an *inventory unit*. Two separate decisions; neither constrains the
        other.
- [x] **Built: `Carve.Chip` takes `hit.Voxel` itself, not a sphere centred on it.** So
      `VoxelHighlight`'s outline stops being an indication of roughly where the bite lands and
      becomes literally the voxel about to be removed. Same hardness gate and same cooldown as
      the sphere — **precision changes how much a swing takes, never what it is strong enough
      to take**, or the modifier would become a way to mine anything one voxel at a time and
      M3's whole progression would be advisory.
- [x] **The shared `Take` is the evidence the gate was unified rather than forked.** Removing
      the hardness check from it fails **9** tests, **seven of them pre-existing sphere tests**.
      One edit breaks both shapes, which is exactly what a second copy would not do.
- [ ] **The modifier key: Ctrl is bound to descend in the mode a builder is most likely in** {D}. `FlyCam`
      binds Ctrl to descend and Alt to crawl, both fly-only — so in **walk** mode Ctrl and Alt
      are free, and in **fly** mode both are taken. Shift is sprint and stays sprint. **Shipped
      on Ctrl as asked, and it is fine in walk mode where Ctrl is free.** The cost is fly mode:
      holding Ctrl descends at `MoveSpeed`, so at the default 6 m/s and `hand_pick`'s 0.3 s
      cooldown a builder sinks **~1.8 m between chips**. **Alt is the better key and the swap is
      one line** — it is free in walk mode and means *crawl* in fly mode, so "slow down to do
      fine work" pairs rather than fights. Left as Ctrl pending a play-test rather than changed
      unasked.
- [x] **Not gated on fly mode**, decided 2026-08-30. A pure modifier, live in both modes. M4c made fly a *measurement instrument* —
      `--profile` implies it and refuses the walk toggle mid-run — so hanging a gameplay
      privilege on it ties a game rule to a profiling flag. There is no creative/survival concept
      in the project yet. Make it a modifier now; add a mode gate if and when game modes exist.
- [x] **`radiusMetres` keeps its meaning exactly**, which is the quiet dividend of leaving the
      sphere as the default: it is still "what a swing takes", the tool JSON is unchanged, and
      no format mods have written against moved. A single-voxel default would have made it "what
      the *modifier* swing takes", which is a format change and not a rename.
- [x] **`--mine` is pinned to the sphere path**, stated in code rather than left to fall out of
      "no key is held". M3d's baseline — 1.2 clones
      and 2.4 KiB per swing, swing cost two orders of magnitude inside a frame — measures the
      *worst* case, and a profile that quietly switched to single-voxel would report a cheaper
      number for the same label and look like an improvement.
- [ ] **Hand mining one block at a time — raised again 2026-08-31, deferred to the tools-and-
      tiers discussion.** The sphere stays the bare click for now. **What changed** since the
      2026-08-30 decision is that the entangled miner exists: "the starting tool is slow" is
      *tedium* when it is the only tool and *progression* when there is a ladder, and yesterday
      there was no top of the ladder. **What is still missing is the middle** — `hand_pick` and
      `steel_pick` are two rungs 0.1 m of radius apart, and then a cliff to a tool that eats the
      world at 60 voxels of reach. **So the entangled miner exposed that the missing piece is the
      ladder, not the granularity**, and the granularity question is downstream of it: answering
      it first sets early-game pacing before the thing that would relieve it exists. **Crafting
      is M11**, which is where a ladder becomes content rather than two JSON files.
      - [ ] **A number the original decision did not have.** 17× was per *voxel removed*. Per
            *metre of walkable tunnel* it is worse, because a single-voxel tool clears a
            cross-section rather than a bore: the player is 0.5 m wide and 1.8 m tall, so ~2×8
            voxels — **16 chips per 0.25 m of advance, 64 per metre, ~19 s per metre** at
            `hand_pick`'s cooldown, over three minutes for a 10 m tunnel. **Estimate, not a
            measurement** — arithmetic off M4a's box and M4b's step height, nobody has dug it.
            Right order, and it says the honest cost is nearer **40× than 17×** for the thing a
            player actually does.
      - [ ] **Deferring is free, which is the whole argument.** `Carve.Chip` and `Carve.Swing`
            both exist and share one hardness gate; which one a bare click calls is **one line in
            `MiningController`**, and `radiusMetres` is content. Cheap late, expensive early.
      - [ ] **Revisit trigger, in Matthew's words: "then we would need to discuss tools and tiers
            etc."** M11 at the latest, sooner if wanted since tools are already JSON. The
            single-voxel default belongs on that agenda rather than settled separately.
- [ ] Collapse debris: loose re-settling rubble vs rigid falling chunks — **M9-PLAN decision 6 recommends loose, dropped straight down, no angle of repose**
- [ ] Power model complexity — decide at M10
      - **Its first consumer is known before it is, 2026-09-03: the entangled miner draws stored
        energy, must be recharged, and battery tiers are the ladder.** See the M10 section.
- [x] Name check: "MinerCraft" — keeping it? **No. The game is Overburden, decided 2026-09-03**
      (DESIGN, locked decisions). The rename is four stages, in order, each its own session:
      - [x] **Stage 1 — the decision, recorded.** DESIGN, this list, the worklog, CLAUDE.md.
      - [x] **Stage 2 — code and projects.** Done 2026-09-03. `MinerCraft.slnx`, the five `.csproj`, the project
            folders, `assembly_name`, and `namespace MinerCraft.*` across 152 files → `Overburden.*`.
            Mechanical: one sed, one build, the whole suite, the default headless baseline
            unchanged to the digit. `start.cmd` and `screenshot.gd` follow. Non-negotiable 1 keeps
            its meaning with the new name — `Overburden.Core` never references Godot.
      - [x] **Stage 3 — the content namespace, and it is the one that can lose data.** Done
            2026-09-03, suite 912 → 926. **The alias sits at `Identifier`'s constructor, not at
            the header read**: `Identifier.RetiredNamespaces` folds `minercraft` to `overburden`
            wherever a string enters — header, mod JSON, dependency list, command line — so the
            two spellings are one value and no reader can forget it. Tested against the played
            world's real v3 header (captured to `Fixtures/played-world-v3-header.bin` before the
            change): the constant alone refuses the file, the alias reads it, and a re-save writes
            the new names with every number kept. `mods/overburden/`, `config/name="Overburden"`,
            `overburden:air` is ID 0. **The `user://` move was done by hand**: `worlds/` copied to
            `app_userdata/Overburden/`, the `MinerCraft/` folder left as the backup — Matthew's to
            delete once he has played the new one. **Two guards the stage found it needed**: a
            table naming one material twice is refused at read, write and seed (the shape a build
            older than the alias writes on quit — it happened, from a stale `.godot/mono` Core DLL,
            and the artefact is `Fixtures/downgraded-world-v3-header.bin`), and a content error
            now refuses the run instead of being played past.
      - [ ] **Stage 4 — Matthew's.** Rename the GitHub repo, rename the local folder, update the
            remote URL, then the docs that name `github.com/MatthewCarven/MinerCraft`.
            **Also the assistant's memory directory**, which is keyed on the working-directory
            path and will not follow a folder rename — copy
            `~/.claude/projects/C--Users-Admin-Desktop---Programming---MinerCraft/memory` to the
            `...---Overburden` key, or the next session starts with none of it.

## Before the repo goes public

Pushed to `github.com/MatthewCarven/MinerCraft` on 2026-08-27, **private**, default branch
`main`. Still not urgent — but the window for the cheap version of some of these is now
open rather than hypothetical, and it closes at "public" or "first collaborator", whichever
comes first.

- [x] **Licence — PolyForm Noncommercial 1.0.0.** Decided 2026-08-26, written to
      [LICENSE.md](LICENSE.md). Source-available, not open source: read, fork, mod and
      share freely; no commercial use. Picked on asymmetry rather than ideology —
      loosening a licence later is one commit, tightening one is impossible, and while
      you hold 100% of the copyright the cost of choosing "too restrictive" is zero.
      - [ ] Revisit trigger: **before merging the first outside PR.** After that the
            licence is effectively frozen without every contributor's agreement, so
            decide then whether to require a DCO sign-off or a CLA.
      - [ ] Revisit trigger: first real art/audio assets (≈M6, or first export) — that
            is when splitting code and content licences starts to earn its complexity.
            Right now every texture is procedural, so there is nothing to split.
      - [ ] Revisit trigger: if you ever want distro packaging or a real contributor
            community, that is the moment to go MIT or Apache-2.0 instead.
      - [ ] Not covered by any licence: the **name**. If "Overburden" survives the name
            check, the protection against someone shipping a clone is trademark and the
            content, not copyright on the engine.
- [ ] **Decide whether commit authorship stays `matthewcarven@gmail.com`** — now the only
      item here with a closing window. All 6 commits carry it. While the repo is private
      and has no other collaborators, rewriting it is genuinely cheap: `git filter-repo
      --mail-map`, then one force-push, and nothing downstream breaks because nothing
      downstream exists. That stops being true the moment it goes public or anyone else
      clones it, after which the address is a permanent public record on every commit.
      GitHub's `users.noreply.github.com` address is the usual alternative, and setting
      `git config user.email` afterwards keeps new commits consistent.
- [ ] Godot's MIT attribution — a credits screen using `Engine.GetLicenseText()` and
      `Engine.GetCopyrightInfo()`. Obligation attaches when the **game** is distributed,
      not when the repo is published, so this belongs with the first export rather than
      here. Recorded so it isn't discovered at release.
- [x] **Sanity-check before the first push — done 2026-08-27, passed.** `reference/`
      tracked 0 files, no `export_credentials.cfg` or `.pck`, 63 files and 122 KiB total,
      which is itself the proof the 51 MB of godot_voxel stayed out.
- [ ] Cosmetic, expected, not a bug: GitHub reports the licence as **"Other"**, because
      PolyForm isn't in its licence database. Nothing to fix unless the badge matters
      enough to switch licences, which it doesn't.

## Needs a human eye

- [x] **Face winding — confirmed correct 2026-08-24.** Visually verified from three
      angles: grass top, dirt sides, stone underneath, solid convex silhouette. Godot's
      clockwise front-face convention is as documented, so no flip was needed. The
      screenshots also confirmed texture-array indexing, per-vertex normals, and the
      UV2.y shade channel — which M5a moved to `COLOR`, see M0 above.
- [ ] Ambient fill is low — faces angled away from the sun go nearly black. Cosmetic
      only, and M5 lighting replaces this shading path entirely, so not worth tuning
      the Environment now. **Owned by M5c, and settled 2026-08-30: the directional light
      goes and the grid is the light** — which also retires the terracing slivers below,
      since those are the sun hitting integer heightmap risers.
- [x] **Streamed terrain confirmed on screen 2026-08-27.** Continuous rolling terrain, no
      seams at chunk boundaries, camera lands above ground. The visible bright and dark
      slivers follow terrain **contours, not the chunk grid** — that is what rules out a
      meshing or streaming seam, which would appear on the 8 m lattice. They are the
      vertical risers of the integer heightmap's terracing, lit or unlit by which way each
      contour segment faces, and they are the concrete symptom of the low-ambient item
      above. Terrain scale matches the settings: ~62 m features, ~12 m amplitude, so at
      r4h's 72 m box roughly one hill fills the view.

---

## Phase 1 — Engine

### M0 — Skeleton ✅ done 2026-08-24, builds clean and runs headless

- [x] Godot 4.7.1 mono project, net8.0, `.slnx` solution
- [x] Project split: `Overburden.Core` (headless sim, no Godot reference — enforced by
      having no Godot dependency at all) / `Overburden.Core.Tests` / `Overburden.Game`
      (Godot front end). The test project exists to prove Core stays headless.
- [x] `VoxelConstants` — 0.25 m voxels, 32³ chunks, power-of-two shift/mask constants
- [x] `VoxelPos` / `ChunkPos` — floor-based chunk/local decomposition via shift and
      mask, correct for negative coordinates. 43 tests, all passing.
- [x] `Texture2DArray` + `Voxel.gdshader` — layer index in UV2.x, light/AO in UV2.y
      (constant 1.0 until M5, but the channel exists now). **Superseded at M5a**: the shade
      needs two independently interpolated channels, so it moved to the vertex `COLOR` array
      and UV2.y is now spare. The M0 reservation still did its job — nothing downstream had
      assumed the channel, and moving it was one line in `ChunkMeshConverter` and one in the
      shader, exactly as predicted.
- [x] Culled block mesh via `RenderingServer`, no `MeshInstance3D`. A 4³ block emits
      192 triangles against 768 naive.
- [x] Flycam — WASD/Space/Ctrl, sprint and crawl, mouse capture
- [x] `.gitignore`, `start.cmd`

### M1 — Storage, identity and the mesher ⚠️ over-invest here

Split into four sessions. Full reasoning in [docs/M1-PLAN.md](docs/M1-PLAN.md).

#### M1a — Identity and registry ✅ done 2026-08-24, 99 tests passing

- [x] `Identifier` — namespaced string IDs (`minercraft:stone`), validated, lowercase
      only. No `enum BlockType`, ever.
- [x] **One numeric ID space, owned by the world, not the run.** `SeedIdTable` restores
      the world's saved assignment; only unseen content gets fresh numbers appended.
- [x] **Never renumber, never compact.** Holes are preserved and never reclaimed; a
      removed mod's IDs resolve to a placeholder retaining the original string ID.
- [x] `minercraft:air` pinned to numeric ID 0 by reserving the *number* before loading —
      the definition still comes from JSON, so the dogfood rule holds
- [x] `Registry<T>` — frozen after the load phase, immutable and lock-free to read after
- [x] `MaterialDefinition` incl. support distance and permeability for M8/M9
- [x] `IContentSource` with directory and in-memory implementations
- [x] `mods/*/mod.json` manifests + `materials/*.json`, errors collected not thrown
- [x] **Base game ships as `mods/minercraft/` through the identical loader**
- [x] Wired into Godot: block textures now come from material JSON
- [x] Tests: identifier validation, freeze, save → remove mod → reload → re-add cycle,
      hole preservation, load determinism, and the counter-example showing ID drift
      *without* a saved table
- [ ] Deferred: dependency topological sort (parsed and validated now, resolved at M12)
- [ ] Deferred: `res://mods` does not resolve inside an exported `.pck` — packaging loose
      mod content needs solving before the first export, not before then

#### M1b — Chunk storage ✅ done 2026-08-25, 133 tests passing

- [x] Three representations in one class: Uniform (one int, **zero allocation**) →
      Palettised bit-packed → Direct `ushort[]` above 256 palette entries
- [x] **Padded to `ulong` word boundaries, no straddling.** Every width used (1, 2, 4, 8)
      divides 64 exactly, so padding wastes literally nothing and every read is one
      load/shift/mask with no branch and no second word fetch.
- [x] Palette widening at 1→2→4→8 bits, repack on boundary crossing
- [x] **Dropped the 16-bit palette tier** the plan called for: a 16-bit palette index
      costs exactly as much as the material ID it points at, so the tier is pure
      overhead. 256 entries is the honest cutover to Direct.
- [x] **Never shrinks eagerly** — `Compact()` only, pinned by a test asserting a chunk
      stays palettised after being mined back to uniform content
- [x] Per-chunk compression palette named distinctly from M1a's per-world ID table
- [x] `CopyTo` bulk read, with a uniform fast path that never touches packed storage
- [x] Tests: every width, exact growth boundaries, exact byte counts, `Compact` narrowing
      and demoting, Direct→Palettised recovery, bounds and 16-bit material validation
- [x] **Differential fuzz against a naive `ushort[]`** — 4 pool sizes × 40k random ops,
      each probing an unrelated voxel so a clobbered neighbour surfaces early, with
      `Compact()` interleaved to prove it never changes content
- [ ] Padded 34³ copy on the sim thread when queueing a mesh job — belongs with M1c,
      which is the thing that consumes it.
      **Reopened 2026-08-26, resolved 2026-08-27: copy on the worker thread instead.**
      godot_voxel needs `shared_ptr` to stop a chunk being freed while a job holds it; in
      C# holding a reference already does that, so GC gives us half the machinery free.
      The remaining hazard is *mutation*, not lifetime — and at M2 there is no concurrent
      writer, because mining is M3 and fluid is M8. So the worker-side copy is safe now,
      keeps the sim thread free, and skips the copy entirely for cancelled jobs.
      - [ ] **Revisit trigger: M3.** The first concurrent writer arrives with mining, and
            needs either a per-chunk lock or a copy-on-write snapshot. Scheduled decision,
            not a bug to discover. See [docs/M2-PLAN.md](docs/M2-PLAN.md).

#### M1c — Greedy mesher ✅ done 2026-08-25, did not spill

- [x] Sweep: 6 face directions × 32 slices, 2D mask per slice, greedy rectangle merge
- [x] **Merge key carries material + light + AO from the start**, constant until M5
- [x] `PaddedChunk` (34³) so boundary faces need no cross-chunk lookup and no locking
- [x] `MaterialTable` — properties flattened into arrays after the registry freezes,
      because the mesher asks "is this opaque?" tens of millions of times a second
- [x] Output `MeshBuffer`/`ChunkMesh` of plain `float[]`/`int[]` — Godot-free
- [x] Tiled UVs — a 32-voxel quad gets UV 0..32
- [x] **Opaque and transparent surfaces separated now**, before water needs it at M8
- [x] Mask allocated once per mesher and buffers reused via `Reset()` — allocation-free
      in steady state, and simpler than pooling since the buffers never escape
- [x] Tests: empty → 0, solid → exactly 6, single voxel → 6, checkerboard → culled count,
      layered terrain → 1
- [x] **Coverage equivalence**: every quad expanded back into voxel faces must reproduce
      the culled mesher's answer exactly, with no cell covered twice. Subsumes both
      surface-area conservation and no-overlap, across 5 random densities.
- [x] Winding invariant: `du × dv` points along the outward normal on every quad

#### M1d — Godot bridge ✅ done 2026-08-25

- [x] `ChunkMeshConverter` — `MeshBuffer` → `ArrayMesh`, the only place Godot types meet
      mesher output, and what makes reusing the buffers safe
- [x] Face table now lives in Core as `VoxelFace`; the Game-side copy and the M0 block
      builder are deleted
- [x] Real chunk of test terrain rendered: layered strata, a rolling surface, and a
      carved cavity so interior geometry is non-trivial
- [x] Measured: 8,208 B for 32,768 voxels; 196,608 naive faces → 6,128 culled → **850
      greedy quads**, 7.2× better than culled and 231× better than naive

### M2 — World

- [x] **Read `reference/godot_voxel/` threaded mesh scheduling and streaming policy.**
      Done 2026-08-26 — findings in
      [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md). It earned its keep again:
      it caught that the job queue below was specified as the wrong data structure.
- [x] **Plan M2 as sub-parts first, the way M1 was** — done 2026-08-27, in
      [docs/M2-PLAN.md](docs/M2-PLAN.md). Four parts, built in order:
      **M2a** job scheduler → **M2b** worldgen → **M2c** streaming policy →
      **M2d** unload path and measurement. M2c is flagged as the one most likely to
      spill into two sessions. The bullets below are grouped by part in that document.
#### M2a — Job scheduler ✅ done 2026-08-27, 186 tests passing

- [x] **How often the "200 ms re-sort" fires — measured 2026-09-06, and there is no timer.** The
      sweep runs inside a worker's `TryTake` once the interval has elapsed, so its rate is the
      workers' wake pattern: 4.6–5.0 sweeps/s in every arm (standing, 12 m/s turning, 40 m/s),
      longest gap 304 ms, because `IdleWaitMillis` (100) has idle workers polling ~80 times a
      second. At rest 590 of 597 sweeps are over an empty list at 2–5 µs; the fill's first sweep
      is the cost, 1,600–1,950 entries at 2.7–3.5 ms once; in-window lock time 0.002–0.010% of a
      worker. `SweepStatistics` under the queue's lock, `sweeps` on the profile, `M2a:` on the
      periodic report. **Revisit trigger: the idle wait changing** — the sweep's rate at rest is
      that number, not the interval.
      - [x] `AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` diagnosed with a control (2 in 5
            failing with the change stashed) and fixed: newest-first before a sweep runs the
            survivor first, and the test disposed on its event with the thrower still queued.

- [x] ~~`System.Threading.Channels` job queue for gen + mesh~~ **Corrected and built.**
      Channels is FIFO, and a FIFO cannot reprioritise queued work as the player moves,
      nor drop work that became irrelevant before it ran. A priority heap is also wrong,
      for the same reason: it orders at insert time, which is when the ordering is least
      valuable. `JobQueue` is a **flat list re-sorted on a timer** (200 ms default), with
      one sweep recomputing priority, dropping cancelled entries, and sorting. Channels is
      used for handing *results* back to the sim thread, which really is FIFO.
- [x] `JobPriority` packs four byte-bands into one `uint`, so comparison is a single
      integer compare. Band0 distance, band2 job class, band1/band3 reserved.
- [x] Generation outranks meshing at equal distance — a mesh job cannot run before the
      data it reads exists, so scheduling it first only burns a worker slot.
- [x] `ViewerSet` reads **without a lock**. Improved on godot_voxel's pre-sized array plus
      atomic count: reference assignment is atomic in .NET, so swapping in a whole new
      snapshot array gives readers a consistent view with **no tearing at all**, which
      their approach cannot. Viewers move at most once a frame, so the allocation is free.
- [x] Real `CancellationToken`s from the start, skipping the distance-drop scheme
      godot_voxel explicitly regrets.
- [x] One pool, two lanes — a `serial` flag for disk work, not a second thread pool.
      Unused until M6; the flag exists now because retrofitting it touches every
      submission site.
- [x] Thread count `clamp(round(0.5 × cores), 1, cores - 1)`. Dedicated threads, not
      `Task.Run` — the .NET thread pool knows nothing about voxel priority.
- [x] **Every submitted job reports exactly one result** — never zero, never two. A job
      that throws faults without taking its worker down; a job still queued at shutdown
      reports Dropped. Fuzzed over 400 jobs with concurrent cancellation, viewer movement
      and sweeping.
- [x] Tests: band precedence, distance evaluation, the **reprioritisation test a FIFO
      cannot pass**, sweep timing, serial-lane exclusion, and the exactly-once invariant.
      27 job tests, all headless in Core.
- [ ] Deferred to M2c-2: nothing submits jobs yet. The scheduler is exercised only by tests
      until streaming feeds it. M2c-1 built the policy that decides *what* to submit; M2c-2
      does the submitting.

#### M2b — Worldgen ✅ done 2026-08-27, 216 tests passing

- [x] Worldgen as a pure function `(seed, chunkPos) → chunk`, reading nothing but its
      arguments — no cache, no neighbour lookup, no field mutated between calls. Pinned by
      an order-independence test, which is really a guard against anyone later adding
      shared mutable state.
- [x] Deterministic seeding via **splitmix64 written out explicitly**, never
      `HashCode.Combine`, whose seed .NET randomises per process. Pinned by
      `KnownInputsHashToKnownOutputs`; `Mix(0)` matches the published splitmix64 value, so
      the test also proves the mixer is the real algorithm rather than a plausible
      rearrangement of it.
- [x] Gradient noise, not value noise — value noise takes its extremes *at* lattice
      points, which shows up on a heightmap as an axis-aligned grid of hills.
- [x] 2D fBm heightmap, strata from a data-driven `StrataTable`, 3D noise caves with a
      surface margin so caves do not bite craters out of the landscape from above.
- [x] Sky fast path: a chunk entirely above terrain returns Uniform air without touching
      memory. Deep chunks are not special-cased — they fill and let `Compact()` notice.
- [x] `GenerationStage` enum and an `ApplyFeatures` hook that deliberately does nothing
      yet, so the two-stage barrier exists before M2c builds the pipeline around it.
- [x] Tests: determinism, seam agreement across a chunk boundary, order independence, the
      sky and deep fast paths, strata ordering, caves-only-remove, and noise continuity.

**Measured** over 2,048 chunks (128 m × 64 m × 128 m):

```
4.0 MiB total, mean ~2 KB/chunk, against 128 MiB naive — 32× better
68% of chunks uniform (803 air, 541 solid), 704 palettised, 0 direct
6.55 ms/chunk with caves, 1.35 ms without — caves are 80% of generation   ← Debug build
```

- [x] **Cave generation was the bottleneck — done at M2d-2, 2026-08-28.** Two free wins were
      taken here at M2b (one avalanche round instead of four chained; 16-entry gradient table
      indexed by `& 15` instead of a 64-bit `% 12`), 9.48 → 6.55 ms/chunk. The rest was
      inherent to evaluating 3D fBm per voxel — ~256,000 hash-and-dot operations per chunk —
      and went at M2d-2 with a coarse lattice and interpolation.
- [x] **That 6.55 ms/chunk was a Debug measurement and nothing said so.** Re-measured on the
      same code: Debug 7.88 / 1.22, Release 2.23 / 0.38. The 80% cave share was right in both,
      so the finding held and only the scale was wrong. `Overburden.Bench` now prints a loud
      warning when built Debug. **Quote a generation figure with its build or not at all.**
- [ ] Caves are blobby pockets rather than connected tunnels, which is what thresholding
      fBm gives you. Connected systems want ridged or worm noise, and are a stage-2
      feature once the pipeline can place things across chunk boundaries.
- [ ] Load strata from mod JSON rather than constructing `StrataTable` in code. The table
      is already data and takes resolved numeric IDs, so this is loader work, not a
      worldgen change.
#### M2c-1 — Streaming policy, as pure data ✅ done 2026-08-27, 254 tests passing

The two-session warning in [docs/M2-PLAN.md](docs/M2-PLAN.md) was taken up front: the
policy is built and fuzzed with no threads and no jobs, so M2c-2 wires the scheduler to
something whose invariants are already pinned.

- [x] `ChunkBox` with **box difference, not volume iteration** — a boundary crossing
      decomposes into at most six disjoint sub-boxes and touches only the shells that
      entered and left. O(R²) per crossing, not O(R³): at radius 8, 289 chunks instead of
      4,913. Verified against brute force over 2,000 random box pairs, asserting both
      halves — the sub-boxes are pairwise **disjoint** and their union is **exact**.
- [x] Intersect-first, so the disjoint case is handled before any slicing. Not a fast
      path: it is what lets the slicing arithmetic skip clamping entirely, and written the
      other way round a non-overlapping box emits slabs with inverted bounds.
- [x] `Viewer` is a Core concept — a position and **two** radii, not a camera. Vertical
      extent is separate from horizontal, so "view distance" is never one scalar.
- [x] **Refcount per chunk across viewers**, built with one viewer. A shared chunk survives
      one of two viewers leaving; the fuzz asserts counts, not just membership, because a
      count drifting high is a chunk that never unloads and a count drifting low unloads
      terrain someone is standing on.
- [x] `ChunkState` enum with a transition guard that **throws** on an illegal edge, rather
      than a scatter of `bool`s. Includes the three edges that are easy to forget: any
      state to Unloaded, one step back when a job reports Dropped, and Meshed back to
      Meshing for M3's re-mesh.
- [x] Dedup lives in the state, not a `HashSet` probe — "already queued" *is* `Queued`, so
      it cannot drift out of sync with the chunk it describes.
- [x] The **stage barrier**: a chunk advances to Features only once all 26 neighbours have
      completed Terrain, and meshes only once all 26 have completed Features.
- [x] **Load margin of two chunks**, derived rather than guessed — see M2-PLAN.md. The
      mesher's padded copy needs the first ring; the feature stage needs the second. Costs
      ~41% more chunks at radius 16, and a parameter rather than a constant so M2d can
      measure both.
- [x] `ChunkStreamer` publishes viewer positions into the `ViewerSet` the job queue sorts
      against, so streaming and scheduling cannot disagree about where the player is.
- [x] Tests: the shell test, two-viewer sharing, radius changes, hysteresis, the barrier at
      the edge of the loaded region, and the fuzz invariant over 400 random viewer
      add/move/resize/remove steps. 38 new tests, all headless in Core.

#### M2c-2 — Wiring streaming to the scheduler ✅ done 2026-08-27, 261 tests passing

**M2 runs end to end.** Terrain streams around the flycam through the job queue and the
greedy mesher; `Main.cs` no longer builds a chunk by hand.

- [x] `GenerateChunkJob` and `MeshChunkJob`, submitted through `SubmitBatch` so the queue
      lock is taken once per frame rather than once per chunk.
- [x] `ChunkPipeline` applies results on the sim thread: `DrainResults` → state transition →
      store voxels or hand the mesh over. Workers only read the snapshot given to them and
      write their own result field; every dictionary and refcount touch is sim-thread.
- [x] **A work list, not a per-frame scan.** A chunk is pushed exactly when something might
      have changed for it, with the dedup flag on the chunk. Advancing one chunk pushes its
      26 neighbours — miss that and the pipeline stalls silently with everything idle.
- [x] **Cancel in-flight work when a refcount hits zero.** Measured under fuzzed movement:
      394 jobs completed against 3,693 dropped, asserted so the test cannot quietly stop
      exercising the path.
- [x] **A chunk with a job in flight is retained, not forgotten.** `ForgetIfUnwanted` collects
      it once the result lands. `ViewedCount` and `TrackedCount` are separate for this
      reason, and a gap between them that does not close is a job that never reported.
- [x] `onChunkViewed` fires on **revival** as well as creation — a chunk that came back mid-job
      would otherwise sit behind a cancelled job forever, leaving a hole exactly where the
      player turned around.
- [x] Stage pipeline driven: Terrain on generate, Features once `CanAdvanceToFeatures`
      passes, mesh once `CanMesh` passes.
- [x] Worker-side neighbourhood gather via `PaddedChunk.CopyFrom` — all 26 neighbours, not
      the 6 that face visibility needs today, because M5's vertex AO samples diagonally.
- [x] Tests: end-to-end generate-and-mesh, the margin generating without meshing, full
      release when the last viewer leaves, leave-and-return mid-load, cancellation on
      teleport, chunk revival, and the no-chunk-left-waiting fuzz. 7 integration tests
      against a real scheduler on real threads.

**Measured** headless from a standing start, r4h/2v + 2 margin on 8 workers:

```
1521 viewed (1521 tracked), 166 drawn, 0 in flight, 0 queued
1926 jobs done / 0 dropped / 0 faulted   — settled inside 5 s
```

1926 = 1521 generates + 405 meshes, and 405 is exactly the 9×5×9 visible box.

- [ ] **Nothing has looked at this on a screen yet** — see "Needs a human eye" above. The
      numbers are right; "looks like terrain" is a different claim.
- [ ] Stage 2 runs on the sim thread, because it writes into its neighbours by design and a
      worker would need a lock over the whole 3×3×3 region. Fine while it places nothing.
      Revisit trigger is the one already recorded for chunk storage: **M3, the first
      concurrent writer.**
- [ ] A faulted job is reported and not retried, so the chunk renders as a hole until the
      player leaves and returns. Untested, deliberately: `WorldGenerator` is a concrete
      dependency with no clean way to induce a fault, and a seam added only for that would
      cost more than the branch is worth.

#### M2d-1 — Unload path and measurement ✅ done 2026-08-27, 261 tests passing

- [x] **Budget mesh *destruction*, not just construction.** `MeshFreeQueue` hides the
      instance immediately and releases a few per frame, with the budget **scaling to the
      backlog** (`max(4, pending/30)`) so a fast traverse cannot outrun it. A count per
      frame, not a time slice, because Godot defers the deallocation and a stopwatch around
      the call reports nothing.
- [x] **`VoxelRenderInstance` never disposed its `ArrayMesh`** — the instance RID was freed
      and the mesh left to the GC finaliser, so the expensive half was happening whenever
      the collector felt like it. Fixed; without it the budget would have controlled only
      the cheap half.
- [x] `StreamingProfiler` with percentile sampling, a scripted surface-following flythrough,
      and `--profile --radius --vradius --margin --speed --warmup --measure`.
- [x] **Load margin measured, and the answer reversed the plan: it is not a tuning knob.**
      Margin 1 at r4h/2v is identical to margin 2 at r3h/1v on every metric — 847 tracked,
      97 drawn, 1.37% of one core. The barrier applies whether or not stage 2 places
      anything, so lowering the margin shrinks the meshable radius rather than buying
      anything. Corrected in four documents. **Keep margin 2; view distance is the knob.**
- [x] Memory and cost while **flying fast enough to force continuous load and unload**,
      swept r2 to r12 — table in [WORKLOG.md](WORKLOG.md).

**Measured**, 40 m/s unless noted:

```
config    chunks   drawn   update p99   main    dropped   memory
r4h/2v      1521     189      1.20 ms   2.50%      712    32.3 MiB
r8h/4v      5733     283      1.26 ms   3.13%    14061    35.2 MiB
r12h/6v    14297     205      1.24 ms   3.11%    56132    31.5 MiB
r12h/6v    14297    2245      1.42 ms   2.97%     5244   122.1 MiB   at 6 m/s
```

- [x] Main-thread cost **plateaus near 3% of one core** from r6 to r12 — bounded by worker
      hand-back rate, not by box size. View distance is not a main-thread cost.
- [x] **Memory is mesh-dominated, ~46 KiB per drawn chunk against ~2 KB of voxel storage.**
      Horizontal radius is expensive (it adds surface); vertical radius is nearly free (it
      adds sky and buried rock that mesh to nothing). The measured argument for separate
      radii.
- [ ] **Not measurable headless: whether the free budget removes the stutter.** Headless has
      no renderer and so no deferred deallocation to spread. Peak queue depth stayed ≤27 in
      every run, so the budget is not itself a bottleneck — but confirming the benefit needs
      a windowed run. Fold into the next visual check.
- [ ] Still open from M1d: `RenderingServer` direct vs `MeshInstance3D`, and whether meshing
      2×2×2 chunks into one surface cuts draw calls enough to be worth it. Both need the
      windowed draw-call counter, which `StreamingProfiler.RenderLine` already prints.
- [x] **Applying mesh results is now budgeted — done 2026-08-29.** `ChunkPipeline.Update`
      applies finished jobs under a 2 ms time slice and leaves the rest for the next frame.
      Time-sliced rather than counted, unlike the free path, because the cost *is* spent
      inside our call. `PendingResults` makes a backlog visible, and `--applybudget=<ms>`
      does the A/B.
- [x] **The 27.6 ms frame M2d-2 saw was not mesh application.** Six runs at r12h/6v and
      40 m/s put frame max between 6.9 and 13.1 ms with no relation to the budget, and the
      worst frame measured all session — 32.3 ms — came from the *most tightly budgeted* arm.
      Frame time in headless is paced by something that is not us; M2d-1 said so and it needed
      saying twice. Use `update ms`, which times `ChunkPipeline.Update` directly.
- [ ] **A time slice cannot bound the tail below the cost of one item.** Update max stays at
      3.3–4.7 ms whether the budget is 0.2 ms or 1000 ms, because one mesh conversion is
      atomic and no budget subdivides it. Bounding the true worst case means capping mesh size
      or splitting the conversion across frames. **Not worth doing on current evidence** —
      recorded so the limitation is known rather than rediscovered. Revisit if a windowed run
      shows the single-mesh cost as a visible hitch, or if M3's incremental remesh makes large
      single meshes common.
- [ ] The 2 ms default is a judgement, not a measurement: applying results averages 0.44 ms
      and hits 2.26 ms at p99 unbudgeted, so 2 ms clips about the worst 1% of frames. Nothing
      measured argues for 2 over 3.

#### M2d-2 — Cave optimisation ✅ done 2026-08-28, 274 tests passing

- [x] **Coarse cave lattice with trilinear interpolation.** `CaveLatticeStep` (default 4)
      samples cave fBm every 4 voxels and interpolates between, so a chunk costs 729 noise
      evaluations instead of 32,768. Release, 2,048 chunks: **2.23 → 0.47 ms/chunk**, caves
      from 83% of generation down to 20%.
- [x] **One definition of the field.** `IsCaveAt` resamples the same lattice rather than
      keeping an "exact" path beside it, and agrees with `GenerateTerrain` bit for bit.
      Two definitions drift, and the symptom is mining a wall that is already air.
- [x] **The step must be a power of two dividing the chunk size**, so a chunk boundary is a
      lattice point and independently generated neighbours interpolate from the same corners.
      The constructor throws otherwise — the damage is a seam, which nothing detects at
      runtime.
- [x] **Measured in the game, not just on the bench.** r12h/6v at 40 m/s: 145 → 731 chunks
      drawn, jobs done 19,738 → 52,120, dropped 30,792 → 16,188.
- [x] `Overburden.Bench` — repeatable worldgen harness over the same 2,048-chunk region M2b
      used, reporting time *and* accuracy, because a speed number without an accuracy number
      is half a trade. `--cavestep=N` on the game does the same A/B in the streaming loop.
- [ ] **The cave threshold has not been re-tuned for the lattice, and should be.**
      Interpolation only ever *removes* caves (measured: voxels gained ≈ 0.001%), because
      linear interpolation cannot exceed its endpoints and the threshold at p95 selects
      exactly the peaks that get clipped. Step 4 costs ~10% of cave volume — 1.444% of rock
      carved down to 1.292%. Recovering it means nudging `CaveThreshold` below 0.42, which is
      a worldgen tuning decision and was deliberately not bundled into an optimisation.
- [ ] Steps 8 and 16 are **not** free lunches despite tiny voxel-disagreement figures (0.54%,
      1.21%): they cost 37% and 83% of cave volume respectively. If the step is ever raised,
      judge it on the `carved` column, not the `lost` column.

#### Settings

- [ ] **Expose view distance as a player option.** `ChunkStreamer.UpdateViewer` already
      handles radius changes by box difference, and there is a test for it, so the mechanism
      exists. Presets grounded in the M2d-1 sweep, at walking speed and **excluding rendering
      cost**: Low r4h/2v (1,521 chunks, ~32 MiB), Medium r8h/4v (5,733), High r10h/5v
      (9,375, ~87 MiB), Ultra r12h/6v (14,297, ~122 MiB). The chunk counts are geometry and
      still hold after M2d-2; what moved is how much of the box actually gets *drawn* while
      moving, which is where the memory figures come from. Re-sweep before writing the
      presets into a settings menu.

### M3 — Mining ← first fun moment

Planned 2026-08-29 and split into four parts in [docs/M3-PLAN.md](docs/M3-PLAN.md). **Read
it before starting** — M3a is a concurrency part and the failure it prevents is a crash on a
worker thread, not a wrong voxel.

#### M3a — The edit path ✅ done 2026-08-29, 294 tests passing

- [x] **Copy-on-write on `VoxelChunk`.** A chunk instance handed to a job is immutable from
      then on; an edit clones it and installs the clone. `ChunkPipeline.Neighbourhood()`
      already captures references on the sim thread, so the worker's snapshot stays valid
      with no lock anywhere on the read path.
- [x] This closes **both** recorded revisit triggers: M1b's "no concurrent writer until M3"
      and M2a's "the worker-side padded copy is safe because nothing writes yet".
- [x] Dirty chunks coalesce into a set drained once per frame — never a remesh per voxel.
- [x] Border rule: an edited voxel at local 0 or 31 on any axis dirties the neighbour on that
      axis too, up to seven of them at a corner. Getting it wrong leaves stale geometry along
      chunk seams, which is where you mine most.
- [x] **Use `band3` of the job priority** for edit remeshes — the band M2a reserved for
      exactly this. Without it the rock you just mined stays on screen while the horizon
      loads.
- [x] Set a `HasEdits` flag on first write. One bit, free now, and it is exactly the set M6
      has to save.
- [x] The fuzz that matters: rewrite every voxel to version *k+1* per edit, so any snapshot a
      worker takes must be **uniform**. A torn read shows two versions and the assertion is
      one line.
- [x] **That fuzz hung instead of failing the first time it was checked**, because its readers
      stopped draining on the first mismatch and deadlocked the writer against a bounded queue.
      Now records and continues, and fails in 356 ms. **Watch a fuzz test fail before trusting
      it** — the same applies to whatever M3b writes against a brute-force reference.
- [x] `ChunkPipeline.ChunkClones` counts the clone rate. M3c gave it something to count; the
      number itself is still unmeasured and M3d settles it, so it is tracked there rather
      than here.

#### M3b — The raycast ✅ done 2026-08-29, 307 tests passing

- [x] Amanatides & Woo voxel DDA. Returns hit voxel, face normal, entry distance, and the
      last empty voxel before the hit — which is where M6's placement goes, so returning it
      now avoids revisiting.
- [x] Fuzzed against brute force the way `ChunkBox` was — but **not** against the fine-step
      sampler this plan called for. A sampler is *less exact than the DDA*: a ray can clip a
      voxel corner for less than one sample step, so it misses voxels the walk correctly
      reports, and the first version of this fuzz failed on the code being right. Replaced
      with an exact ray/AABB slab test, which is also a genuinely different algorithm.
- [x] Watched fail before being trusted: flipping the boundary calculation breaks it in 7 ms.
- [x] **An unloaded chunk must stop the ray, not read as air.** Otherwise the player targets
      a voxel in a chunk that does not exist and the edit vanishes with nothing to report.
- [x] Ray starting inside solid material returns that voxel with no normal, or the first
      swing mines whatever the camera is embedded in.

#### M3c — The carve ✅ done 2026-08-29, 364 tests passing

- [x] Sphere by voxel-centre distance test, radius in metres from the tool. `Carve.Sphere`
      in `Overburden.Core/Mining/`, with `Carve.Swing` reading its two numbers off a
      `ToolDefinition`. **"Sub-voxel" means the voxel is small, not partly full.**
- [x] **The distance test is the definition; the bounding box is only an optimisation.** The
      box is `VoxelPos.FromMetres(c ± r)` — provably sufficient, correct at negative
      coordinates for free, and reusing a flooring primitive that is already pinned. Do not
      replace it with a hand-derived tight bound: the first version was one, and its obvious
      break-recipe (floor → cast) can only *widen* the box, so it could never fail a test.
- [x] Hardness gates removal per voxel. `MaterialTable.Hardness` projects it now, and the two
      shipped tiers straddle dirt and stone so the gate is visible: `hand_pick` leaves a stone
      seam standing in a dirt bank, `steel_pick` takes it.
- [x] Tool tiers load from `mods/minercraft/tools/*.json` through the same loader materials
      use — non-negotiable 5. The JSON was not cut; two tiers ship, because **one tier cannot
      demonstrate a gate**.
- [x] `CarveResult` reports removed, too-hard and unreachable separately. **Unreachable is not
      too-hard**, for the same reason `RayOutcome.Blocked` is not `Missed`.
- [x] **A tool radius is a denial-of-service vector, and a NaN threshold is worse.** The scan
      is cubic, so `"radiusMetres": 1000` freezes the game rather than carving a crater;
      `ToolDefinition.MaxRadiusMetres` bounds it at load *and* in the carve. It is **4 m and
      an estimate** — 16 m was the first answer and did not bound the failure its own comment
      named. A NaN threshold fails the other way, *open*: `hardness > NaN` is false, so the
      gate inverts from "takes nothing" to "takes everything".
- [x] **`ToolDefinition.Unknown` is inert twice over**, and was inert zero times when first
      written. Radius 0 still met the distance test on a swing landing exactly on a voxel
      centre, and threshold 0 took anything of hardness 0. The test that should have caught
      it swung at a voxel *corner*. Non-positive radius now returns early, and the threshold
      is negative infinity.
- [x] **A viewer leaving mid-remesh stranded the mesh on screen.** `Meshed → Meshing` is an
      M3 edge; before it, "is it `Meshed`?" and "does the renderer hold a mesh?" were the same
      question. `StreamedChunk.HasLiveMesh` is the flag now. Mine, fly away, and the pre-edit
      geometry used to keep drawing for unloaded terrain.
- [x] **The base game's own files are loaded by a test at last.** `ShippedContentTests` reads
      `Overburden.Game/mods/` from disk — every other content test builds a fixture, which had
      already drifted. It checks the shipped *numbers* too, because the hardness gate is a
      property of the content and no code test finds a JSON file edited to make it pointless.
- [x] Eight mutations watched to fail before the tests were trusted. One of them exposed a
      *comment* that overclaimed: the pipeline test's "eight chunks remeshed" does not catch
      the border rule failing, because the writes span eight chunks by themselves. Split out a
      one-voxel corner test that does. **A green test is not evidence its comment is true.**
- [ ] Not measured yet: the clone rate under a held button. `ChunkPipeline.ChunkClones` counts
      it and a carve finally drives it, but nothing holds the button — that is M3d.

#### M3d — Binding, feel, measurement ✅ done 2026-08-29, 367 tests passing

- [x] Left mouse mines on the flycam, number keys pick a tier (**superseded at M6d-3: the
      number row is the material hotbar and the tier moved to T**), Esc stops both looking and
      mining. `MiningController` is Godot-free so `--mine` can drive it headlessly.
- [x] Wireframe highlight on the targeted voxel, one `RenderingServer` instance moved rather
      than churned. **Verified in a windowed run 2026-08-29** {D} it draws where it should, so
      the inflation is enough to keep the lines off the faces they outline.
- [x] **The swing is centred on the hit voxel's centre**, not the ray's entry point. The
      entry point lies on a face, so half the sphere starts in air and the reach depends on
      the angle of incidence — a radius in a mod's JSON would mean something different every
      swing. `Carve.SwingCentre`, in Core, because where a swing lands is a game rule.
- [x] **Answered: yes, a pick swing fits in a frame, with room to spare.** Mining
      continuously at 5 swings/s while flying r6h/3v at 8 m/s moves `update ms` p99 from
      0.910 to 1.038 ms and the main thread from 0.80% to 1.02% of one core, against a
      16.7 ms frame. **Do not build** the dirty-region mesher, smaller mesh blocks, or a
      latency-hiding scheme until something asks for them.
- [x] **M3a's clone estimate settled: right per chunk, 7× pessimistic per swing.** Measured
      1.2 clones and 2.4 KiB per swing against a guess of 8 chunks and ~16 KB. Eight chunks
      is the worst case, at a corner; a 0.5 m sphere usually stays inside one.
- [x] The raycast and carve themselves are 0.044 ms mean / 0.17 ms max. The cost is all in
      the remesh, which is on workers, and its apply is already budgeted.
- [x] **The headless frame-time trap, third time.** A mining run showed `frame ms` max
      19.6 ms against a 6.9 ms control while `update ms` max moved 0.13 ms, and it did not
      reproduce twice. Use `update ms`.

#### Surfaced, not M3c's or M3d's

- [ ] `JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` failed once under
      heavy machine load and passes otherwise. It is a one-worker scheduler test with a
      wall-clock `Patience`, so it is load-sensitive by construction — worth knowing before
      it fires in CI and gets read as a real fault.
- [ ] `MaterialTable` indexes raw, so a material ID past its `Count` throws
      `IndexOutOfRangeException`. The mesher has the same exposure and would hit it first, but
      the carve is the first consumer a *player* triggers, so the same latent bug becomes a
      crash on click rather than at load. The fix is validating chunk contents at load: M6.

#### Open decisions

- [ ] **Instant removal or progressive damage?** M3 ships instantaneous with a per-tier
      cooldown, and the thing is now in hand — so this is answerable by playing it, which is
      Matthew's call rather than a measurement. Progressive needs per-voxel damage state,
      which chunk storage has nowhere to put, so it wants its own design pass rather than a
      field bolted on. `ToolDefinition.CooldownSeconds` already exists either way.
- [x] **Played 2026-08-29: mining feels good.** The verdict M3 was actually waiting on, and
      the one no profile could give.
- [ ] **Reach reads a little long at 5 m**, which is twenty voxels. Fine for a creative-mode
      flycam, so unchanged {D} but **reach is the player's, not the tool's**, and M4 is where
      something exists to own it. A survival reach wants to be shorter. **M4c moved it to
      `PlayerController.ReachMetres`** — still 5 m and still deliberately not in the tool JSON.
      **Settled by play 2026-08-30: 5 m stays**, with survival mode as the recorded trigger to
      shorten it to 3 m or 2 m.
- [ ] **Is the bite the right size, and does the hardness gate read?** `hand_pick` (0.4 m,
      leaves stone) against `steel_pick` (0.5 m, takes it). Numbers, not code — they are two
      JSON files, so tuning them needs no rebuild.
- [ ] **Where is the swing centred — the ray's entry point, or the hit voxel's centre?** The
      entry point lies exactly on a voxel face, so about half the sphere lands in air and the
      effective reach depends on the incidence angle; the voxel centre is grid-aligned and
      stable under repeated swings but makes the crosshair feel like it lies near a voxel
      edge. **`CarveResult.AlreadyAir` against `Candidates` is the number that settles it**,
      which is why both are reported. M3d decides, with the thing in hand.
- [ ] **Edits are lost on unload at M3** — a chunk regenerates from the seed, so flying far
      enough erases the mine. Acceptable until M6, recorded so it is not filed as an edit-path
      bug.

### M4 — Player ✅ **done 2026-08-30**

Planned 2026-08-29 and split into four parts in [docs/M4-PLAN.md](docs/M4-PLAN.md). **Read it
before starting.** The structural point: until now the viewer's position was an *input* and
the streamer followed it. From M4 the position is an *output* of a query against the very
chunks the viewer decides to load, and an unloaded voxel read as air means the player falls
out of the world and accelerates away from anything that could stop them.

#### M4a — The collider ✅ **done 2026-08-29**

`Overburden.Core/Physics/` holds `Aabb` and `VoxelCollider`. 33 new tests, suite 367 → 400.

- [x] AABB vs voxel grid, **axis-separated** resolution (X, then Y, then Z). A combined sweep
      snags on every voxel boundary of a flat wall; axis separation makes sliding the default.
- [x] Do **not** use Godot physics for terrain (`ConcavePolygonShape3D` is a trap)
- [x] **Substep so no substep exceeds 0.2 m**, under one voxel. Move-then-resolve is exact
      below 15 m/s (0.25 m ÷ 1/60 s) and silently tunnels above it — so **walking never
      tunnels and falling always does**, which is the worst possible shape for a bug.
- [x] The substep cap is on **displacement, not the box's dimensions**. A 0.25 m floor is
      thinner than the player. On the vector's *magnitude*, not per axis — 0.2 m on each of
      three axes is 0.35 m of travel, and there is a test for exactly that.
- [x] **An unloaded voxel is solid.** `ChunkStreamer.TryGetMaterial` hands back air *and*
      false; a caller that ignores the return value gets the unrecoverable failure. Same
      distinction as `RayOutcome.Blocked` vs `Missed`, worse consequence.
- [x] Solidity is a predicate — `id != air && !IsFluid(id)` — not a comparison against air.
      `MaterialTable.IsFluid` already exists, so this is correct now *and* at M8, and needs no
      revisit trigger the way `VoxelRay` did.
- [x] `grounded` is an **output of the solve** ("the −Y resolve fired"), not a separate probe.
      One step of latency for step-up, against ~40 extra voxel reads per step to remove it.
      `MoveResult.Grounded`, plus `HitCeiling` and `BlockedHorizontally` for M4b.
- [x] **Resting contact:** resolve to the face minus an epsilon, overlap is a strict
      inequality. The bug that survives into shipped games is a millimetre of jitter with
      `grounded` flickering and jump misfiring — not tunnelling.
- [x] **Which of those two is load-bearing was measured, and it is the strict inequality.**
      Setting `ContactEpsilonMetres` to zero leaves the whole suite green: a face exactly on a
      voxel plane is touching, not overlapping, and voxel planes are exact in binary. The
      epsilon is kept as belt and braces, and its honest cost is recorded below.
- [x] **`Aabb` stores a minimum corner and a size, not two corners**, and that is what makes
      standing still bitwise stable. A resolve places one face from the blocking voxel's index
      and carries the size across untouched; deriving the size as `Max − Min` every step would
      round it differently each time and a resting player would creep.
- [x] Lives in `Overburden.Core/Physics/`, `System.Numerics`. Where a player may stand is a
      world rule a multiplayer server must agree about.
- [x] **The fuzz with the sharp oracle: seal the player in a room** of solid voxels and apply
      thousands of random displacements, including magnitudes far past terminal velocity.
      Escaping is a boolean — no reference implementation, no tolerance. **Watched to fail:**
      raising `MaxSubstepMetres` to 1.0 m fails 14 of the 33 tests, the fuzz among them.
- [x] Corner-snag test: sliding diagonally along a flat wall travels the full tangential
      component. Fails if someone "simplifies" the resolve back into one pass — watched, by
      cancelling every axis when any one is blocked.
- [x] Shared postcondition asserted in *every* motion test, not only the fuzz: the box never
      ends a step overlapping solid. Lives in the `Step()` helper, so it cannot be forgotten.
- [x] Standing still for 10,000 steps does not drift and `grounded` never flickers. One
      settle of 0.1 mm on the first step, then bitwise nothing.
- [x] Determinism: identical input sequence, bitwise-identical trajectory. The whole of the
      multiplayer claim, and cheap now. Compared through `SingleToInt32Bits`, not `==`.
- [x] **Measured, so M4d does not have to: a walking step costs 41 `TryGetMaterial` calls**,
      not the guessed ~400, and the worst alignment is 63 per substep — 252 at a four-substep
      terminal-velocity fall. A resolve reads one voxel *layer*, not the whole box. **No
      memoisation, no spatial structure**; the guess was pessimistic and the fix it proposed is
      not needed.
      - The layer counts are not the 16 + 4 + 16 the box's nominal 2 × 8 × 2 suggests: a 0.5 m
        box that is not grid-aligned straddles **three** voxel columns, not two.

##### Found while building it, and worth keeping

- [x] **The voxel layer is taken from the *moved* box, never from "one past where the box
      was".** The two are the same layer while the box is outside the wall, so nothing in the
      suite could tell them apart — until a test was added for a box that *starts* inside one.
      The second form finds no new layer ahead of an already-overlapping box and waves the move
      straight through, walking it out the far side of the wall in about ten steps. It also
      cannot teleport the box forward past what the move asked for.
- [ ] **Knife edge: a gap exactly the box's width.** The resolve stops the leading face an
      epsilon short of contact, so a 0.5 m box in a 0.5 m gap has nowhere for that margin to
      come from and the trailing face ends 0.1 mm inside the far wall. It cannot pass through
      either side, and a straight drop down such a shaft is exact because an axis with no
      displacement is never resolved — both pinned by tests. Only reachable if a corridor is
      dug to precisely the player's width; `hand_pick` leaves 0.75 m. Setting the epsilon to
      zero would remove it, at the cost of the defence it exists to provide.
- [ ] **Float positions cap the usable world at a few kilometres**, long before `VoxelPos`'s
      `int` does. One float ULP passes the contact epsilon around ±800 m. **Revisit trigger:**
      a world meant to be walked further than that wants a *floating origin*, which is what
      every large-world engine ends up building — not a wider float, which `VoxelPos` would
      outrun anyway.
- [x] **A displacement needing more than 1,024 substeps is refused**, the same class of bound
      as `ToolDefinition.MaxRadiusMetres`. 204 m in one step is a velocity that has blown up,
      not a fast body, and the alternative is a loop long enough to look like a freeze. An
      estimate, and labelled one: it bounds a failure, it does not describe a limit.
- [x] **`VoxelCollider.OverlapsSolid` is public**, because M4c's unstick rule needs exactly it
      and so does every motion test's postcondition. `ResolveAxis` already gives partial
      unstick — it clears the layer the leading face is in — but not the full rule, which
      stays M4c's.

#### M4b — Motion ✅ **done 2026-08-29**

`Overburden.Core/Physics/` gains `PlayerMotion` and `PlayerMotionConfig`. 35 new tests, suite
400 → 435.

- [x] Gravity, jump, terminal velocity, step-up **and step-down**. `PlayerMotion.Step` is a pure
      function of a state, an intent and a world — no clock, no accumulator, no field.
- [x] **60 Hz fixed step, which is not the 20 Hz sim tick.** 20 Hz is 50 ms of input latency.
      Machines are simulated; the player is driven. Core's step takes `dt` so a test or a
      headless profile can drive it, and nothing in Core knows the rate.
- [x] Constants, **all estimates and labelled so in their own remarks**, replaced by playing at
      M4d: box 0.5 × 1.8 × 0.5 m, eye 1.65 m, gravity 25 m/s², jump 7.5 m/s, walk 4.5 m/s,
      sprint 7 m/s, terminal 40 m/s, step height 0.5 m.
- [x] **The apex is 1.1875 m, not the 1.125 m the plan derived** — measured to four decimals.
      `v²/2g` is the continuous answer; a jump that *replaces* its step's gravity rather than
      being reduced by it overshoots by exactly half a step of launch speed, `v·dt/2` = 0.0625 m.
      So the clearance over a four-voxel ledge is **0.1875 m**, half again what the plan claimed
      — and it is **rate-dependent**, twice as much at 30 Hz. The apex test asserts one-sided,
      because the direction is a design claim rather than an accident.
- [x] **Reach did not move here.** The plan's table lists it under M4b; it is not a body
      dimension, it is what an arm does, and it stays on `MiningController` until M4c has a
      player to own it. `EyeHeightMetres` *is* in the config, because it is the third dimension
      of the same body — splitting those across two files is how one goes missing.
- [ ] **Terminal velocity is a streaming parameter in a physics costume.** 16–24 m of loaded
      world below the player is crossed in 0.4–0.6 s at 40 m/s. Still M4d's to pick, and see the
      missing signal below.
- [x] **The player must fit down their own tunnel.** `hand_pick` (0.4 m) leaves a 0.75 m free
      bore; a 0.5 m box passes with 0.125 m either side. **Player width and the weakest tool's
      bite are coupled** and neither can be changed alone. Written into `WidthMetres`'s remarks
      where a person changing it will read it.
- [x] Step-up, with all four guards and a test each: grounded only, non-upward only, the
      horizontal re-attempt must **fully** succeed, and the whole box needs headroom at the
      lifted position. **The refusal tests change exactly one thing** against a climb that
      succeeds — a refusal test that builds its own scenario proves the body did not climb, not
      that the guard is why.
- [x] The headroom guard is **structural rather than a separate check**: the lift and the
      re-attempt are real `Move` calls and a `Move` that did not start overlapping cannot end
      overlapping. Tested anyway, because a guarantee that comes from somewhere else is the kind
      that quietly stops holding.
- [x] Step-down, the half people forget: without it a downhill slope makes the player airborne
      every few steps and the camera bobs continuously. Guards mirror step-up's, and a staircase
      of single voxels is now walked up **and back down** without ever leaving the ground.
- [x] Coyote time, jump buffering and variable jump height are M4d's and are additive — the
      model does not change to accept them. Jump is level-triggered for now, so holding the
      control bounces; edge-triggering needs the previous frame's button in `PlayerState`, which
      is jump buffering's field.

##### Found while building it, and worth keeping

- [x] **The lift snap is what keeps M4a's epsilon claim true, and nothing caught removing it.**
      An unobstructed lift is placed at its exact target rather than at accumulated substeps.
      With `ContactEpsilonMetres` in place that changes no outcome — a resting body floats 0.1 mm
      up and that swamps a few ULP. But M4a *recorded* that the epsilon can be set to zero with
      the suite green, and with the lift accumulated instead of snapped, zeroing it **fails four
      tests**. A documented finding in the previous milestone was one line away from going stale
      with nothing failing to say so. Now caught without touching the epsilon, by a test that
      places the feet exactly on a voxel plane — which is what M4c's spawn and a fly toggle both
      do.
- [x] **A step-up settles by exactly what it lifted**, so it is a climb and never a fall. What
      that protects is step-down's distance limit: a settle that could go further would give the
      same rule two answers depending on whether the body happened to climb something first.
      Nothing caught it either, because reaching it needs a body that clears what it just climbed
      in one step — 0.075 m of walking against a 0.5 m body cannot, so the test uses a 45 m/s
      body and says why.
- [ ] **Recorded cost: step-up's "fully succeed" guard makes wall-hugging stop you dead.**
      *Not encountered in the 2026-08-30 play-test, but that test walked up hills rather than
      into corners — the case needs provoking deliberately and has not been.*
      Walking diagonally into a corner where one axis is a climbable lip and the other is a tall
      wall refuses the climb, because the lifted re-attempt is still blocked on the wall. That is
      the plan's specified behaviour and it is safe. **If M4d finds it reads as stuck, the
      relaxation is one line:** require the re-attempt to be blocked on no axis the original move
      was not already blocked on. Pinned by a test either way.
- [x] **Built at M4d: `MoveResult.BlockedByUnloaded`**, the signal telling "stopped by rock"
      from "stopped by an unloaded chunk" — exactly *steps spent held*, the number that picks
      terminal velocity. **Deferring it at M4b was right:** an unread flag with no profiler
      consuming it is a number waiting to be believed, and built alongside its consumer it was
      correct first time, while M4d's ground-flicker counter — written with no consumer in mind
      — was wrong twice.
- [x] **One `StepHeightMetres` for both directions**, because stepping down a lip you just
      stepped up should not behave differently. **Revisit trigger:** M4d, if a 0.5 m instant drop
      reads as a jolt where the matching rise does not. The split is one field and one call site.
- [x] **Horizontal velocity is replaced from intent every step, not accelerated.** No friction,
      no momentum, full air control — the snappiest model and the one whose faults are easiest to
      judge by playing. Acceleration, air control and ground friction are three separate feel
      decisions and a person makes them. The blocked-axis zeroing is currently only honest
      reporting and is kept because it stops being cosmetic the moment M4d adds acceleration.
- [x] Cost, as a regression detector rather than a measurement: a walking step is **one `Move`**
      and M4a's ~41 voxel reads; a successful step-up is **four** — the failed attempt, the lift,
      the re-attempt, the settle.
- [x] `TestWorld` moved out of `VoxelColliderTests` into a shared `PhysicsTestHelpers`, for the
      reason `ShippedContentTests` exists. The property it preserves is that an unloaded chunk
      answers *air **and** false*; a second copy that drifted would let a body which ignores the
      return value pass its own suite.

#### M4c — Binding ✅ **done 2026-08-29**

- [x] **Do not delete the flycam.** `FlyCam`'s own comment says M4 replaces it; that comment
      is wrong and following it destroys the measurement apparatus M2d and M3d were built to
      create. `--profile` assigns `GlobalPosition` directly and `AdvanceFlythrough` snaps the
      camera every frame; a camera parented to a physics body cannot do either.
      `FlyCam.FlightEnabled` is the whole of it: false in walk mode, and **only the movement
      is switched off — mouse-look stays live in both modes**, because looking around is the
      same act whichever body you are and because walking needs a yaw to walk along.
- [x] Walk/fly toggle on **F**, **`--profile` implies fly**, body inert in fly mode. A profile
      run also *refuses* the toggle mid-run, because handing a scripted flythrough's camera to
      a body invalidates the measurement rather than producing an interesting one. Verified:
      the default headless run reports **1521 viewed / 165 drawn / 1926 jobs**, M2d's recorded
      numbers unchanged to the digit.
- [x] Order within a step: **player → viewer → pipeline**. The step is in `_PhysicsProcess` and
      the rest in `_Process`; Godot runs every physics tick of a frame before that frame's
      `_Process`, so the order holds without a manual ordering.
- [x] **`ReachMetres` moved to `PlayerController`**, closing M3d's recorded item.
      `MiningController.ReachMetres` is now `required` and defaultless, so there is no second
      number to drift — `PlayerController.DefaultReachMetres` is the only literal.
      **Settled at 5 m by play 2026-08-30**, so the plan's 3.5 m estimate was never shipped.
      At the time of the move it was deliberately left alone: the plan's table said 3.5 m and the
      M3 play-test said 5 m read long, both were feel, and retuning in the same pass as the move
      would have made it impossible to tell which change did what. Play decided it a day later.
- [x] **Spawn is the cycle at its worst**, and the order is **gate, then unstick, then step**.
      `PlayerSpawn.IsWorldReadyFor` checks every chunk the box overlaps *and the chunk beneath
      each of them*; `FeetOnSurface` places the feet on the row above the surface voxel;
      `TryUnstick` lifts plane by plane to a 4 m bound. Measured: **13–14 steps held for the
      world**, about 0.22 s, on the default headless run — it moves run to run with worker
      scheduling, so it is a scale and not a constant.
- [x] **Unsticking before the gate is the unrecoverable order.** Against an unloaded world every
      candidate is solid, the search runs to its bound, and a body standing in open sky is
      reported stuck. `UnstickFailsAgainstAnUnloadedWorld` pins it.
- [x] **An unstick lifts and never drops** — a body with its head in a ceiling goes up through
      it and onto the roof, not down out of it. Up is the only direction whose answer does not
      depend on which face was overlapping, and a body that can be pushed down by material
      above it can be pushed into the floor. **Caught by nothing until a test was written for
      it**; see the two green recipes below.
- [x] Mining the floor from under yourself works for free and is correct. **Recorded revisit
      trigger:** material appearing *inside* the box has no answer at M4 — M6's placement can
      do it deliberately, M9's collapse by accident — and `PlayerSpawn.TryUnstick` is its seed,
      including its up-only decision.
- [ ] Not a bug, and will be reported as one: walking into your own tunnel is walking into the
      dark until M5.

##### Six break-recipes, two green, and they are two different things

- [x] Caught: the gate always saying yes (3 tests), the gate dropping the chunk beneath (1, and
      nothing else), unstick accepting the first candidate blindly (5), `FeetOnSurface` using
      the surface row rather than the row above (3).
- [x] **Green, and a real hole: unstick starting at the box's own voxel row.** Every buried-body
      test placed the feet on a plane already, so the extra downward candidate was the box
      itself and overlapped by construction. `UnstickNeverMovesABodyDownward` closes it.
- [x] **Green, and a documentation correction: `Aabb.Translated` instead of rebuilding the
      box.** No failing case exists — `Min.Y + (y − Min.Y)` is exact for every pair a 4 m lift
      can produce inside the few-kilometre world M4a's float analysis allows, and the horizontal
      `+ 0.0f` is exact for every float except `−0.0f`, which `Aabb.FromFeet` cannot produce.
      Kept for rule-consistency with M4a's corner-and-size storage, **not** because a test can
      tell. Same shape as M4a's zeroable epsilon.
- [x] **Feet exactly on a voxel plane survives exactly one step.** The end-to-end spawn test was
      written expecting bitwise identity and was wrong: the first −Y resolve places the body
      `ContactEpsilonMetres` clear, so 1.25 m becomes 1.2501 m — the number the headless run
      prints. The state exists only at spawn and at a mode toggle, which is why M4b's lift test
      constructs it by hand. The *second* step is bitwise identical, so rest is stable.

##### What M4b hands M4c, so it isn't rediscovered at the binding

- [ ] **`PlayerState.AtFeet` is deliberately *not* grounded.** Nothing has looked yet; the first
      step's gravity finds the floor and says so. A spawn that hands back `Grounded: true` lets
      step-down fire before the body has ever been anywhere.
- [ ] **Spawn and the fly→walk toggle both place the feet exactly on a voxel plane**, because
      neither goes through a resolve that would add the contact epsilon. That is the exact case
      `ALiftOfExactlyTheStepHeightClearsALipOfExactlyTheStepHeight` pins — so do not nudge the
      spawn by an epsilon to make it look tidy, and do not be alarmed that the first step settles
      by 0.1 mm.
- [ ] **The camera and the mining ray share one origin:** `PlayerState.EyePosition(config)`.
      Bind both from that call rather than deriving the offset twice, which is how a half-height
      goes missing in one of them.
- [ ] **`dt` comes from `_PhysicsProcess`; Core does not know the rate.** `project.godot` sets no
      `physics_ticks_per_second`, so it is Godot's default 60 Hz, which is what M4b's constants
      were measured at. **Changing it changes the jump height** — the apex overshoot is
      `v·dt/2`, so a 30 Hz tick jumps 6 cm higher. Leave it alone or re-measure.

#### M4d — Feel and measurement ✅ **done 2026-08-30**

- [x] **Answered: no, the player cannot outrun the streamer — not at any shipped setting.**
      `held 0` at terminal 40 m/s at r4h/2v, r6h/1v, r6h/2v, r6h/3v and r12h/3v, and at r6h/3v
      it survives **20, 40, 60 and 90 m/s** with zero held steps. **Terminal velocity stays
      40 m/s**, now with at least 2.25× of measured margin rather than an estimate.
- [x] **And where it *does* break, because "never happened" is not a measurement.** r12h/3v at
      90 m/s holds for 3 steps (0.42%, longest run 17 ms); r16h/3v at 120 m/s for 12 (1.67%,
      longest 83 ms). The arithmetic agrees: one chunk row at r6 is 13×13 = 169 chunks crossed
      every 0.2 s at 40 m/s, and at r16 it is 33×33 = 1,089 crossed every 0.067 s at 120 m/s.
- [x] `--fall`, `--fallheight` and `--terminal` added. **`--terminal` because sweeping the
      constant the profile exists to pick should not need a rebuild** — same argument as
      `--radius`.
- [x] **`MoveResult.BlockedByUnloaded` built**, the signal M4b deliberately deferred. A subset
      of `Blocked` by construction, so it cannot contradict it. 7 tests, five break-recipes,
      four caught.
- [x] **A falling step costs 16 voxel reads, not M4a's predicted 252** — and the reason is
      `ResolveAxis`'s zero-delta early-out, which M4a added so a resting body would not creep by
      a rounding error. A pure fall has exactly zero X and Z displacement, so two axes return
      before querying and only the Y layer is scanned: 2×2 voxels × 4 substeps. **A decision made
      for numerical stability turned out to be a performance one**, so do not "simplify" it away.
      Exact across the sweep: 8.0 reads at 20 m/s, 16.0 at 40, 20.0 at 60, 31.7 at 90.
- [x] **Collider cost is three orders of magnitude inside a frame**: `step ms` mean 0.013–0.015,
      max 0.57, against 16.7 ms. **Do not build** the query memoisation M4-PLAN named.
- [x] **Time it directly; do not read `frame ms`.** Fourth confirmation: `frame ms` sat at mean
      6.90 / max 7.48 across every run while directly-timed `update ms` max ranged 1.1–9.3 ms.
      The two numbers are unrelated.
- [x] **The flicker detector produced two artefacts before it was worth anything**, and both
      reported a small non-zero number that would have read as a real defect:
      - **It counted landings.** The landing step *is* the first at-rest step and the one before
        it was a fall. Fixed by making the test asymmetric — *was at rest, and is now not
        grounded*, which a landing cannot satisfy.
      - **It counted held steps.** A body on an unloaded chunk is grounded with velocity zeroed,
        so it looked like rest; the step where the chunk arrived and proved to be air was then a
        textbook flicker. The r16/120 run reported 5 over 28 "resting" steps, every one of them
        the held-then-released pattern the *next column* was already counting.
      Both found by reading a non-zero number and refusing to believe it. `RestingSteps` is now
      printed beside the flicker count, and the report says outright when the detector never ran.
- [ ] **Recorded limitation: the fall profile measures generation, not meshing.** A falling
      player falls through *air*, so every run reports `0 drawn`. The 250 m configuration lands
      three times and is the closest available to a player falling down a shaft they dug, where
      terrain would be in the box on all sides. **Revisit if meshing under vertical motion ever
      matters** — it would need a dug shaft, not a drop from altitude.
- [x] **Dropped jobs are the M2a scheduler working**: 31,421 at r12 and 47,478 at r16, cancelling
      stale work as the viewer outruns it. First time anything has moved fast enough to exercise
      the argument M2's reference reading bought against a FIFO.
- [x] **Played 2026-08-30: the jump feels good.** The first of M4b's constants to get a verdict
      from a person rather than a number, and it is the one the plan was least sure of. 7.5 m/s
      against 25 m/s² stands, and the 1.1875 m apex is a keeper.
- [ ] **Jump height may become an option, possibly with optional double jump** {D}. Two notes so
      neither is re-derived:
      - **A "jump height" setting is not `JumpSpeedMetresPerSecond` under a different name.**
        Inverting the apex needs the *discrete* answer, `v²/2g + v·dt/2`, not the continuous
        `v²/2g` — and the second term is rate-dependent, so a height expressed in metres is only
        honest at a stated tick rate. Solve for `v` with the overshoot in, or the setting reads
        6 cm short at 60 Hz and 12 cm short at 30.
      - **Double jump is additive and cheap: one counter on `PlayerState`.** It is the same
        category M4b already named for coyote time and jump buffering — `PlayerState`'s "three
        fields and no more" comment anticipates exactly this, and `Step` stays a pure function
        with a fourth. It does not disturb step-down's non-upward guard, which already refuses
        while the body is rising.
- [x] **Played 2026-08-30: 0.25 m ground walks like ground.** "Walking uphill occurred
      naturally, I did not need to jump." This is the question M4d existed to ask and the one no
      profile could answer — DESIGN's claim that step-up is *not* a convenience here but the
      difference between walking on the ground and not is confirmed by a person. **Step height
      stays 0.5 m.**
- [ ] **Two step-up/step-down corner cases are still unjudged, and ordinary walking cannot reach
      them.** Neither was reported, which is silence rather than evidence:
      - **Guard 3's wall-hug** needs walking *diagonally* into a corner where one axis is a
        climbable lip and the other a tall wall. It is the case that would read as stuck, and it
        will not turn up by walking up a hill.
      - **Step-down's 0.5 m instant drop** needs a lip walked *off*, not up. The revisit trigger
        was always "does the drop jolt where the matching rise does not".
- [x] **Played 2026-08-30: you "sortof" fit down a tunnel you carve.** The coupling M4-PLAN named
      holds, and the hedge is exactly what its arithmetic predicts: `hand_pick`'s 0.4 m radius
      leaves a 0.75 m bore, a 0.5 m box passes with **0.125 m either side — half a voxel**, and
      consecutive swings centred on different voxel centres wander the bore's axis by more than
      nothing. So it fits on paper and scrapes in practice.
- [ ] **If the tunnel wants to feel roomier, the cheap lever is the tool, not the player** {D}.
      `hand_pick`'s `radiusMetres` 0.4 → 0.5 makes the bore five voxels, 1.25 m, clearing 0.375 m
      either side. It is **one number in one JSON file and needs no rebuild**, which is what M3c
      made tool tiers content for. Widening the *player* instead is the move M4-PLAN warns
      against, because width is the half of the coupling that is not free.
- [x] **Played 2026-08-30: 5 m of reach stays.** "Generous but I'll allow" — and generous is
      the right direction to err in a creative sandbox, where the cost of over-reach is a
      slightly floaty crosshair and the cost of under-reach is not being able to touch what you
      are looking at. **This closes the last of M4d's feel questions.**
- [ ] **Revisit trigger: a survival mode wants 3 m or 2 m** {D}. Not a tuning note — the number
      is right for *this* mode and wrong for a mode that does not exist yet, so it changes when
      the mode does, not before.
- [ ] **Two things now wait on a game-mode concept**, and that is the interesting part: reach
      (5 m creative / 2–3 m survival) and whether the precision mining modifier should be gated
      to creative at all. Neither justifies building modes on its own; **two is when the shape of
      the thing starts being knowable**, and a third would make it overdue. Recorded so the count
      is visible rather than rediscovered.

#### Deliberately not in M4

- [ ] Swimming and fluid drag — M8. The solidity predicate already reads `IsFluid`, so wading
      is the default rather than a special case.
- [ ] Falling damage — M11, where health is. Dropped items — M6, and DESIGN is explicit that
      Godot physics is fine for those.
- [ ] Placement colliding with the player — M6, using M4c's unstick rule.
- [ ] Non-cube collision shapes (slabs, ramps, ladders) — all "a voxel is one material or
      another" violations wearing different hats; M3c already argued that one.
- [ ] `PlayerTick` on the event bus, and multiplayer prediction. A fixed-step solve in Core
      with a determinism test is the whole of what M4 owes them.

### M5 — Lighting ✅ **done 2026-08-31**

Planned 2026-08-30 and split into four parts in [docs/M5-PLAN.md](docs/M5-PLAN.md). **Read it
before starting.** The structural point: light is the **first thing in this engine that is not a
function of one chunk**. Worldgen is pure per chunk, meshing takes a padded copy, a carve is
local — light propagates arbitrarily far, and removing one voxel can change light in a chunk
that is not a neighbour.

**The constant that makes it tractable, and it is exact rather than lucky:** 4-bit light reaches
15 m; the load margin is 2 chunks, 16 m. A source outside the loaded region contributes
`max(0, 15 − 16) = 0` at the visible box, so light computed over the loaded region is *exact*
inside the drawn one. **The margin now has a third load-bearing reason** (mesher ring, feature
stage, light reach) and it is the tightest of the three. If light ever goes to 5 bits — 31 m —
the margin must go to 4 chunks.

#### M5a — Vertex AO and the merge key — **done 2026-08-30**

- [x] Per-corner AO from the padded chunk's diagonals: `(side1 && side2) ? 0 : 3 - (side1 +
      side2 + corner)`. The `side1 && side2` case is the difference between a corner that reads
      as a corner and one that reads as a smudge. `Overburden.Core/Meshing/AmbientOcclusion.cs`.
- [x] **`PaddedChunk.CopyFrom`'s 26 neighbours were gathered for exactly this** and nothing
      about the gather changed. Every diagonal sample lands inside −1..32 by construction: the
      axis coordinate is the neighbour's, and the two in-plane coordinates move ±1 from 0..31.
- [x] **Two light channels, not one, and DESIGN forces it.** `CornerShade` carries
      `shadeSky = skylight × AO` and `shadeBlock = blocklight × AO`, 8 bits each, four corners
      packed into one `ulong`.
- [x] **AO multiplies into both**, so a diurnal cycle stays a uniform on `shadeSky` and costs no
      repropagation.
- [x] **Verified where the two bytes live, against Godot 4.7.1 rather than assumed — and the
      answer is `COLOR` for a stronger reason than the plan had.** Two probes: (1)
      `AddSurfaceFromArrays` with no flags produces a surface **without**
      `ARRAY_FLAG_COMPRESS_ATTRIBUTES`, so UV2 stays float32 and the packed value round-trips
      exactly; colours round-trip exactly at 8 bits too. (2) A render probe shows **no sRGB
      conversion is applied to `COLOR` under a `ShaderMaterial`** — the two paths differ by one
      output byte, which is 8-bit quantisation, where a decode would be ~0.23 apart.
      **So precision does not decide it, and packing is wrong anyway: the hardware interpolates
      the packed number.** The fractional part of an interpolated sky channel lands in the block
      channel's byte, so two corners one sky level apart sweep block light through its whole
      range in between. Per-vertex shade exists *to be interpolated*; a packed pair of
      independently varying channels cannot be. `R = shadeSky`, `G = shadeBlock`, two spare.
- [x] **Merge key `(Material, CornerShade)`**, and a sharper statement of why merging is exact
      than the plan had: adjacent faces **share** two corners, and an independently derived shade
      at a shared corner is the same number by definition — so key equality does not merely
      permit a constant shade along a merged run, it **implies** one. Pinned by
      `AMergedQuadHasConstantShadeAlongEveryAxisItMergedOn`.
- [x] **The diagonal flip**, implemented on the **sum of both shade channels** rather than on AO
      alone. Identical at M5a (block light is zero, sky is AO-monotone) and correct afterwards: a
      flat floor with a torch has constant AO and a shade gradient, and an AO-only test would
      never fire on the surface most likely to show the crease. Still a pure function of the
      merge key, so every face merged into a quad agrees which diagonal it takes.
- [x] Only `IsOpaque` materials occlude, so water is still an M8 question.
- [x] **`--ao=0` A/B taken, from a standing start so the chunk set is deterministic.**
      r4h/2v, 165 drawn: **262 → 492 quads per chunk, 1.88×**. r12h/3v @ 6 m/s, 1543 drawn:
      253 → 472, **1.87×**, static memory 94.8 → 150.4 MiB and `update ms` p99 1.39 → 2.05.
      Streaming unchanged: 1521 viewed / 165 drawn / 1926 jobs, M2d's figures to the digit.
- [x] **The 2–4× estimate ran pessimistic, as every estimate here has.** Measured 1.88×. Mesh
      memory grew 1.59× rather than 1.88× because a chunk's memory is not all mesh; solving the
      two r12 runs for a constant baseline gives **~42 KiB of mesh per drawn chunk with AO off**,
      independently reproducing M2d's ~46 KiB, and ~79 KiB with it on. Extrapolated to r12's
      2,245-chunk case that is ~205 MiB against the estimated 200–400 MiB.
- [x] **The AO-alone baseline for M5's fragment-shader trigger is 492 quads per drawn chunk**
      at r4h/2v. The trigger is light in the merge key costing more than 2× the quads of AO
      alone, so M5c must stay under ~984.
- [x] Tests: merge exactness against an independently written AO oracle, the constant-shade
      property, a solid chunk still emitting 6 quads, the inside corner reading 0, the complete
      occlusion truth table, and both triangles facing outwards in either flip state. 468 → 496.
- [x] **Break-recipes run, and two were not what the plan expected.** Swap corner sign table 8
      failed, drop `corner` 4, drop the `side1 && side2` case 5, sample the diagonal on one axis
      6, remove `CornerShade` from the key 8, force the flip off 6, wind the flipped triangles
      backwards 2. But: **swapping `side1`/`side2` is a no-op, not a hole** — the rule is
      symmetric in its two sides, since both `&&` and `+` commute. The meaningful version is
      mislabelling *which corner* gets which sample. And the plan's "the flip must not disturb
      `EveryQuadIsWoundConsistentlyWithItsNormal`" is structurally unfalsifiable: that test reads
      vertex positions and the flip changes indices only, so
      `BothTrianglesOfEveryQuadFaceOutwardsInEitherFlipState` was added to walk the indices.
- [x] **`ChunkPipelineTests.AnEditIsWrittenAndTheChunkIsRemeshed` had a comment that was never
      true.** "Somewhere buried" meant "not on the chunk boundary", and the first solid voxel in
      scan order has air on three sides — carving it is a change of exactly zero quads. It passed
      until M5a shifted the surrounding merges and the two totals came out equal.
      `FindSolidInterior` now checks all six neighbours.
- [ ] **Needs a human eye: is the AO ramp right?** `AmbientOcclusion.LevelShade` is a linear
      ramp bottoming at 0.4 — `[102, 153, 204, 255]` — chosen for being obvious rather than
      measured, and labelled a feel constant. The only part that is not taste: the four values
      must stay distinct, because they are the merge key. `screenshot.gd` makes the A/B
      repeatable; M5d judges it.

#### M5b — The light grid, propagation, and the un-light pass — **done 2026-08-30**

- [x] `ChunkLight`: 8³ = 512 cells per chunk at 1 m, 4-bit sky + 4-bit block, **512 bytes** of
      propagated values. **The derived side is 1.5 KB, not the planned 1 KB**, and the extra is
      not optional: the sky-source bit and the emission nibble are what let the un-light walk
      zero a cell without erasing the lamp it is walking away from. Still nothing against ~46 KiB
      of mesh.
- [x] **`[z][x][y]`, Y contiguous** — `LightCellPos.Index`, the same shape as `VoxelPos.Index`.
- [x] `VoxelsPerCell` a **power of two dividing 32**, checked in a static constructor *and*
      asserted by a test, because a constant that is currently right still needs the rule written
      where it can be read.
- [x] **Conductance is a property of the cell *face*.** Light crosses iff some voxel pair across
      the shared 4×4 boundary is non-opaque on both sides. Both wrong rules have a test each and
      both were run as break-recipes: "any opaque → cell opaque" fails 13 tests, "any air → face
      open" fails 3.
- [x] **Recorded cost stands**: the value is still per cell, so rock within a metre of a lit
      tunnel glows slightly. The lever if M5d says it reads badly is a 0.5 m cell, which
      `LightCellPos` already permits.
- [x] **Skylight seeded analytically** from `ISurfaceHeights.SurfaceHeightAt`, which
      `WorldGenerator` now implements. The interface exists so a test can state a flat world in
      one line rather than reverse-engineer a seed that produces one.
- [x] `SkyColumn` per chunk column, 32×32 `short`, seeded from the heightmap and lowered by
      edits. **Its downward scan is bounded by the loaded region rather than by its own limit**,
      because an unloaded voxel reads as opaque — the collider's rule, for the collider's reason.
      `MaxScanVoxels` is a backstop for a fixture that answers "air" forever and says so.
- [x] **A cell is a sky source only if *every* one of its sixteen columns is open, not any.**
      Under-seeding is recoverable — the cell above is a source, the face between them conducts
      through the open half, and full skylight falls through it for nothing — so a cliff face is
      lit by propagation anyway. Over-seeding is not: the value is per cell, so a mostly-solid
      cell declared a source pushes daylight into the rock through its own faces. Breaking it to
      "any" fails 3 tests.
- [x] **The lifetime half stays in M5c**, as split. What is here is the rules, which are pure.
- [x] **The oracle reached the sky-height path**, and the fuzz asserts it did: `breakthroughs > 4`
      printed beside the result, because a sequence that stayed underground would pass for the
      wrong reason.
- [x] Skylight rule: **full value straight down with no attenuation**, −1 per metre otherwise.
      **The free fall is restricted to full brightness**, and that restriction needed a test
      shape nothing else in the suite had — see the break-recipe note below.
- [x] `MaterialDefinition.LightEmission` (0..15), projected into `MaterialTable`, **refused
      rather than clamped at load** exactly as an out-of-range tool radius is, and clamped again
      in the table because the loader is not the only way a definition arrives.
      `mods/minercraft/materials/lamp.json` ships at 15.
- [x] **Debug bind: L places a lamp** in the empty voxel in front of the crosshair, on a key-down
      edge. One key, one material, no inventory, no cooldown. `Hit.Previous` is what M3b built
      and labelled "where M6's placement goes"; this is its first caller. Resolved by identifier
      and disabled if the base game does not ship a lamp — *not* falling back to air, which would
      carve a hole out of whatever the player was pointing at.
- [x] `ShippedContentTests.SomethingShippedActuallyEmitsLight` — something emits, in 1..15,
      nothing emissive is air, and something reaches the top of the range so the 0..15 decision
      is exercised by the content rather than only by the format.
- [x] **The un-light pass**, two queues, with the re-seed step. Dropping the re-seed fails 7
      tests including the oracle, which is exactly what M5-PLAN said to check.
- [x] **A better oracle than the one planned, and by a category.** "Incremental equals
      from-scratch" runs the *same add pass* as the subject, so it can only independently catch
      the un-light pass. The propagation invariant
      `V = max(S, max over conducting neighbours (V(n) − attenuation))` has a **unique** fixpoint,
      so checking it cell by cell is complete correctness with no propagation in the check at all
      — same category as M4a's sealed room. Both are asserted, plus a third that fell out free:
      incrementally maintained sky heights must equal a fresh downward scan of the voxels.
- [x] Tests: analytic falloff as an equality; a 0.25 m wall blocks and a 0.75 m bore conducts;
      an open column is full sky to the surface and dark below; a shaft stays lit however deep;
      cross-chunk propagation with no seam; determinism, bitwise. 496 → 518.
- [x] **Break-recipes: twelve run, ten bit, and the two that did not were different problems.**
      - **The six-neighbour expansion in `Invalidate` was provably dead work.** It existed for
        the closing-face case, and removing it fails nothing — because conductance is recorded on
        *both* cells of a face, so a face that changes always reports both in `BuildDerived`'s
        changed set. Deleted, the caller's contract written into the doc comment, and the
        property pinned by `SealingAFaceReportsBothOfItsCells`. Breaking the reporting instead
        fails 11 tests, which is what makes the one-cell version safe rather than lucky.
      - **"Any skylight value falls unattenuated" was a genuine hole.** A shaft straight down from
        the surface carries *full* skylight, which falls free under both rules, so it cannot tell
        them apart. The shape that can is a horizontal branch off the shaft with a **second shaft
        dropping from its far end**, fed at 10 rather than 15. Built, and the recipe now fails.
      - **One recipe hangs rather than failing:** dropping the "only if greater" guard in the add
        pass, exactly as predicted. The battery treats a timeout as its own outcome so it does
        not read as infrastructure trouble.
- [x] **The test fixture was half the problem and its shape is M5c's.** The first run took 4.5
      minutes and crashed the test host; it is 6 s now. Three causes, and the third is a design
      finding rather than a test one: **the rebuild scope is two different shapes.** A voxel's
      *conductance* reaches only its own chunk plus a neighbour across whichever faces it
      actually touches — an interior voxel, 30 in 32, rebuilds exactly one chunk. A change to the
      column's *sky height* is not local at all: it moves the sky flag the whole way up and down
      that chunk column and nowhere else. **M5c should implement exactly those two scopes.**

#### M5c — The pipeline: the stage, the sample, and the relight

**Split into M5c-1 (the stage and the relight) and M5c-2 (the sample and the shader), on the
same grounds M2c and M2d were split: everything in the first half is verifiable headlessly and
nothing in the second half is. M5c-1 done 2026-08-30, suite 518 → 542.**

##### M5c-1 — the stage, the lifetime, and the relight — **done 2026-08-30**

- [x] `GenerationStage.Lit` as a third rung; `CanMesh` requires the chunk and all 26 neighbours
      to be there. Nothing about `ChunkState` changed — no new job kind, no new transition.
- [x] `ChunkStreamer` implements `ILightChunks` and owns both lifetimes: a `ChunkLight` per chunk,
      and a `SkyColumn` per (x, z) **refcounted separately**, so a vertical unload does not take
      one away while chunks above still share it.
- [x] **Light needs no third margin ring, and that was the M5c-1 finding.** Following the existing
      ladder — mesh at R needs R+1 lit, lit at R+1 needs R+2 at `Features`, `Features` at R+2
      needs R+3 — the margin would have had to grow from 2 to 3, which is +17% chunks on top of
      M2c's +41%. **Deriving a chunk's light asks for voxels, not for a stage**, so the outermost
      loaded ring derives its cells and joins the propagation while never reaching `Lit` and never
      meshing. That is exactly the arrangement the margin's third load-bearing reason describes.
- [x] **Revisit trigger, and it is real: when `ApplyFeatures` starts placing anything, it must
      route through the relight path the way `SetVoxel` does.** Deriving at `Terrain` is sound
      today only because the feature stage writes nothing. A feature that writes into a neighbour
      is an edit by another name, and the machinery to repair it already exists.
- [x] **A push into a chunk with no light grid is dropped, and nothing remembered having tried.**
      The BFS pushes rather than pulls, which is correct while that chunk will never exist and a
      lost update the moment it arrives a few frames later. `LightPropagator.SpreadInto` re-spreads
      the six adjacent face layers whenever a grid comes into existence. **M5b could not have found
      this**: every fixture there builds all its grids before lighting any of them, which the
      streamer cannot do.
- [x] **Six face layers, not the whole 488-cell shell.** Light crosses faces only, so the twelve
      edges and eight corners cannot conduct into the chunk at all — a statement about the model
      rather than a saving.
- [x] **The first deterministic test for that bug passed on broken code**, because it dropped the
      chunk under a sky shaft where every cell is its own full sky source and relights itself. A
      lamp in the *neighbouring* chunk is the shape that separates the arms. Fourth time this
      project's "watch it fail" rule has earned itself.
- [x] **Two flags, as settled: `NeedsRemesh` is stale, `RemeshUrgent` is the player is waiting.**
      A relight sets only the first, and only on a chunk that has actually drawn something.
- [x] `IsQuiescent` moved onto the pipeline. A caller testing only jobs and the work list saw
      quiescence while every chunk sat one stage short of meshing — which is what a stalled light
      stage looks like from outside.
- [x] **Break-recipe battery: 12 recipes, 9 caught, 3 holes — and no two holes were the same
      kind.** "Advancing to `Lit` does not push its 26 neighbours" fails **14** tests, which is
      M2c-2's rule being load-bearing somewhere new.
- [x] **Hole 1 — nothing asserted the two-flag split, only the sign of the counters.** Putting
      `bool urgent = chunk.NeedsRemesh` back into `SubmitMesh` left the suite green, because the
      relight remeshes merely got counted as edit remeshes too and both stayed non-zero.
      `OnlyTheChunkHoldingTheLampIsUrgent` asserts the *number*.
- [x] **Hole 2 — a scenario that made both arms agree.** On a still world the derive mask never
      moves, so gain-only and inequality are the identity. Two overlapping viewers separate them:
      removing the second is all losses and no gains. `LosingANeighbourReDerivesNothing`.
- [x] **Hole 3 — the shaft test dug a one-voxel bore, and a one-voxel bore is never a sky
      source.** A cell is open to the sky only when **all sixteen** of its voxel columns are, so
      the bore's light arrived by free fall and conductance alone could take it away — the
      sky-height rebuild never mattered to it. A **cell-wide** shaft makes every cell down it a
      source in its own right, so capping the top must clear flags in chunks nobody edited.
      `CappingACellWideShaftPutsTheDarkBackInChunksTheCapIsNotIn`, which asserts the deep cell is
      in a different chunk from the cap so it cannot silently stop covering that.
- [x] **M5-PLAN warned about this case and the warning was not enough.** It asked the fuzz to
      include edits breaking through a column's topmost opaque voxel — and that path *did* run,
      the column *did* move, and the test still passed for the wrong reason. **A warning about
      which code path runs is not a warning about which property is checked.**
- [x] **Light propagates on the sim thread under a time budget**, one global cell queue writing
      into the streamer's grids. Cross-chunk propagation then needs no special case, because the
      algorithm has no per-chunk boundary. Same precedent and same recorded reason as
      `ApplyFeatures`. Time-sliced rather than counted, following the mesh-*apply* budget rather
      than the mesh-free one: this is our own CPU work start to finish, so it can be measured as
      it is spent. Resuming is free because the queues are fields.
- [x] **The plan costed the wrong half, and by an order of magnitude.** M5-PLAN estimated ~779k
      cell visits and ~15 ms for the fill and said nothing about deriving. **Measured in Release,
      per chunk:** surface 0.245 ms, deep 0.327 ms, open sky 0.204 ms — call it 0.27 ms, so
      **~0.41 s for the whole 1,521-chunk fill** against generation's 0.47 ms a chunk on eight
      workers.
- [x] **The budget is 4 ms, and a standing-start sweep is what says so.** Light delays the
      *first draw*, because nothing meshes until its neighbourhood is lit. At the 5 s report:
      1 ms → 857/1521 derived, 0 drawn; 2 ms → 1467/1521, 0 drawn; **4 ms → settled, 165 drawn**;
      8 ms identical. **2 ms was a real regression and invisible in the tests**, which pass their
      own generous budget. It is a ceiling and not a reservation — once lit, nothing is queued
      and the step costs nothing. `--lightbudget=` makes the sweep a shell loop, the fourth seam
      of that shape after `--cavestep`, `--applybudget` and `--ao`.
- [x] **The headless contract is unchanged to the digit**: 1521 viewed (1521 tracked), 165 drawn,
      1926 jobs, 492 quads per drawn chunk — M2d's and M5a's figures with light in the barrier,
      which is what keeps M5d comparable with everything before it.
- [x] **Real-world light numbers**, which the test world got wrong in both directions:
      `1521 derives / 1521 tracked (299 uniform, 20%), 585,012 cells visited, 169 sky columns,
      0 relight remeshes`. Exactly one derive per chunk; the closed form covers **20%**, not the
      test world's 12% nor M2b's 68%; **585k visits against the plan's estimated 779k — the
      plan's propagation arithmetic was good, and it was not the half that mattered**; and zero
      relight remeshes during the fill, which is the priority ordering staying intact.
- [x] **`PaddedChunk.CopyFrom` is 60–75% of that, not the derivation** — it recomputes the
      neighbour index per voxel where 27 block copies would do. **Named as the lever and
      deliberately not built**, per the rule; it would speed up meshing too, since a mesh job
      builds one per chunk as well. **M5d-1 measured it at 59%** — the bottom of this estimate —
      **and says build it**, because the budget cannot buy back a cost that exceeds the frame.
- [x] **A uniform neighbourhood has a closed form and skips the copy entirely** — every face
      conducts iff the material is not opaque, every cell emits what the material emits. Pinned
      against the general path cell for cell and face for face, the `IsCaveAt`/`GenerateTerrain`
      shape of test, because two derivations of one field is two derivations that can drift.
      **Coverage measured at only 12% in the test world**, which is all surface; M2b's 68% uniform
      figure is for chunks alone and this needs the six face neighbours uniform *and equal*.
      **Measured at M5d-1, and it is not one number:** 20% at a standing start, 14% over the
      bench's fixed region, and **5–21% in motion** — falling through mostly-uniform air reaches
      55%. The coverage is a property of where the viewer is, which is why a single figure was
      never going to be the answer.
- [x] **The suite went 8 s → 83 s and back to 16 s.** Deriving is real work and `Settle` sleeps a
      millisecond a pass, so a production-sized light slice turned every pipeline test into a few
      hundred extra round trips. `PipelineHarness.GenerousLightBudget` is the fix; a test that
      wants a starved budget passes its own.
- [ ] Rejected: a light job over a 3×3×3 snapshot (half-width 12 m against a 15 m reach — a
      source 14 m out is silently missed, and the seam appears only near light sources); 5×5×5
      (125 references per job); per-chunk iterative boundary exchange (data-dependent stopping
      condition); a worker with a lock held for the whole job.
- [ ] **A third `GenerationStage`, not a new `ChunkState`.** Light is a barrier, not a job, so
      `CanMesh` gains one clause and nothing about the job pipeline changes.
- [x] **What sets it — settled 2026-08-30: derived data built *and* the propagator drained.** A
      per-chunk stage whose condition carries one global term, because **a global BFS has no
      per-chunk completion event**. Two things have to hold and only one is per chunk: every
      chunk within light's ~2-chunk reach has derived data (the margin's third load-bearing
      reason, cashed in), and the propagation has actually run (global, because the queue is).
- [x] **Rejected: a per-chunk count of queued cells.** It is the obvious way to make the temporal
      half local and it is wrong in the direction testing does not show — a chunk with no queued
      cells of its own can have a neighbour's frontier one step from spilling in, so the count
      reads settled while the light is still moving.
- [x] **The global term is not a stall at any rate this game reaches**, estimated: ~779k cell
      visits for the initial fill at r4h/2v, ~15 ms, ~15 frames under a 1 ms budget — against the
      ~0.22 s the spawn gate already costs. A new chunk column in flight is a few chunks; a swing
      is a handful of cells. **All estimates; M5d measures the first.**
- [x] **Being wrong is recoverable by the path that has to exist anyway.** Light changing in an
      already-meshed chunk — a chunk loads two away, or the player breaks through — is caught by
      the drain's changed-cell set marking a remesh. Streaming and mining share one mechanism, and
      that is what lets the global term be as coarse as it is.
- [ ] **Advancing one chunk must push its 26 neighbours** — M2c-2's rule, and light arriving may
      be the last thing a neighbourhood was waiting on. Miss it and the pipeline stalls silently
      with everything idle.
- [ ] Padded light grid **10³ = 1,000 bytes, covering −1…8**: a vertex at the far corner sits at
      cell coordinate 8.0 and the interior centres run 0.5…7.5, so it needs cell 8 from the
      neighbour.
- [x] **Built on the sim thread at submission, not on the worker — settled 2026-08-30, and this
      corrects the plan.** A `ChunkLight` reference is not safe to hand a worker. Holding 27
      `VoxelChunk` references is safe because **voxels are copy-on-write since M3a**; light is
      not, and the propagator writes into these 512-byte arrays in place while a mesh job runs.
      Copy-on-write for light means thousands of clones per drain against **one 1,000-byte copy
      per mesh job** — 0.6% of the 157 KB padded voxel copy the worker still builds itself.
- [x] **Recorded because it reads as an inconsistency with `PaddedChunk` and invites being
      "fixed".** The two are built in different places for reasons that do not transfer either
      way.
- [ ] Trilinear sample per vertex, multiplied into AO to form the corner shade byte. Light and AO
      differing but their product matching merges anyway — harmless and slightly beneficial.
- [ ] **Re-light on edit marks every chunk whose padded light grid holds a changed cell** — the
      owner plus, on a boundary, its neighbour. Marking only the owner is the break-recipe and
      the symptom is a light seam on the chunk grid. M3a's `Neighbourhood()` marks all 27 for the
      analogous reason.
- [ ] **Most swings change no light at all** — a 0.4 m sphere inside a 1 m cell does not open a
      cell face. Mining is not a relighting storm; breaking *through* is. A light placed in open
      air is the 125-chunk case and is a one-off.
- [ ] **Try the cheap mitigation first:** a light-only remesh does not need band3 urgency. Light
      100 ms stale is invisible.
- [x] **It is two flags, not one line — settled 2026-08-30, and this corrects the plan.**
      `NeedsRemesh` carries both *the mesh is stale* and *the player is waiting on it*, and
      `SubmitMesh` reads the one flag for both — right while an edit is the only thing that can
      invalidate a mesh, and a relight is the second thing. `NeedsRemesh` keeps the first meaning,
      a new `RemeshUrgent` carries the second; an edit sets both, a relight sets only the first.
- [x] **A relight only marks a chunk that has actually drawn something.** A chunk that has never
      meshed needs no marking — the barrier already holds it until its light exists, and marking
      it would make its *first* mesh urgent. During the initial fill every chunk's light changes,
      so the whole distance-ordered queue would arrive at band3 and M2a's ordering would be
      silently gone. Nothing looks broken; the world just loads in the wrong order.
- [ ] **Revisit trigger for moving light out of the vertex entirely** (per-chunk 3D texture,
      hardware trilinear, *no remesh on a light change*): light in the merge key costs more than
      **2×** the quads of AO alone, **or** breaking into a lit space costs more than ~16 chunk
      remeshes at a rate mining can sustain. Not built first because it contradicts a DESIGN line,
      needs a material per drawn chunk or a clipmap volume, and every named fallback in this
      project so far has proved unnecessary when measured.
- [ ] **The directional light goes — settled 2026-08-30. The grid is the light.**
      `Voxel.gdshader` renders `unshaded`:
      `ALBEDO = tex * faceTint(NORMAL) * (shadeSky*skyTint*skyBrightness + shadeBlock*blockTint)`.
- [ ] **The face tint is computed in the shader from `NORMAL`, not baked into the vertex** — top
      brightest, sides middling, bottom darkest, the cheap fake every voxel game uses. Keeping it
      in the shader keeps a *look* constant that will certainly be tuned out of the merge key,
      and it costs nothing because a quad has exactly one normal.
- [ ] **`skyBrightness` is a uniform with no day/night cycle behind it yet.** It exists because
      the alternative — scaling the skylight seed and reflooding — relights the world every
      frame.
- [ ] **This retires two long-standing open items**: the low ambient fill, and the bright and
      dark slivers following terrain contours. Both are the sun hitting the integer heightmap's
      risers; with no sun the terracing goes back to being geometry rather than a lighting
      artefact. What stays a look question for M5d is whether the face-tint constants are right,
      not whether the sun goes.
- [ ] Tests: the barrier holds a chunk until its 26 neighbours are lit; **exact remesh scope,
      asserted in both directions** (over-marking is a performance bug that hides correctness
      bugs); unload releases the grid and revival recomputes it; an edit landing mid-mesh
      re-marks; the no-chunk-left-waiting fuzz extended over the new stage.

##### M5c-2 — the sample, the shader, and the sun — **done 2026-08-30**

- [x] `PaddedLight` gathered on the sim thread at submission; trilinear sample per vertex;
      occlusion multiplied into both channels; `Voxel.gdshader` `unshaded` with a
      `NORMAL`-derived face tint and a `sky_brightness` uniform; `DirectionalLight3D` removed
      from `Main.tscn`. Suite 542 → 556. **Lighting is end to end — a lamp glows.**
- [x] **The headline number is the opposite of what the plan expected.** Quads per drawn chunk at
      r4h/2v: **262** (AO off, light off) / **492** (AO on, light off — the M5a baseline) /
      **283** (AO off, light on) / **404** (AO on, light on). **Light in the merge key does not
      cost quads, it saves 18% of them**, and mesh memory follows quads.
- [x] **The mechanism: the shade is `occlusion × light`, so where light is zero the product is
      zero whatever the occlusion was.** Faces that could not merge under AO alone merge freely
      in the dark, which is most of a mine's surface area. The merge stays exact — the shades
      really are equal. Pinned by `InDarknessEveryOcclusionLevelCollapsesToTheSameShade`, because
      the quad measurement now depends on it and an ambient floor added to the shade *byte*
      rather than to the shader would silently undo it.
- [x] **So the fragment-shader fallback is retired, not deferred.** Its trigger was ">2× the
      quads of AO alone", i.e. ~984; measured 404, which is **0.82×**. Fifth named fallback in
      this project dissolved by measurement, and the first dissolved in the wrong direction.
- [x] **The plan's 10³ padded grid is one ring short: it is 11³ over −1…9.** The derivation is
      right for a sample taken *at* the vertex, and the sample is taken **half a cell along the
      face normal**, which pushes the far face to 8.5. **The offset is not a refinement** — a
      face sits on the boundary between solid and open, so without it a flat fully lit floor
      renders at 7.5 of 15 everywhere and looks like an art choice. `ChunkLight` recorded the
      offset at M5b and nothing fed it back into the grid size.
- [x] Cell 9's interpolation weight is **exactly zero** — the face-axis sample is `plane / 4 ± 0.5`
      with `plane` an integer, so at `plane == 32` the weight is 8.0 − 8 = 0 in binary. Clamping
      the index instead would be right today and silently wrong the first time the cell size or
      the offset changes, for 331 bytes.
- [x] **The sun going retired two long-standing open items**: the bright and dark slivers
      following terrain contours (the sun on the integer heightmap's risers), and the low ambient
      fill. Face tint is top 1.0 / ±Z 0.8 / ±X 0.65 / bottom 0.5, **in the shader and not the
      vertex** — a quad has one normal so it is free, and baking it would put a look constant
      into the merge key where retuning it means remeshing the world.
- [x] **Three screenshots, because one would have proved less.** Daylight (skylight, AO, tint, no
      slivers); `--skybrightness=0` (terrain **pure black** — sky is what lights the world, block
      light is genuinely zero, and darkness has no accidental ambient); and
      `--skybrightness=0 --debuglamp` (a warm pool over ~15 m and nothing else lit). **Only the
      third can prove `COLOR.g` carries block light and is tinted separately** — every other test
      in the milestone would pass identically if both channels went into one byte.
- [x] `--skybrightness=` and `--debuglamp` are the new seams, so the look questions are
      repeatable rather than a recollection. No ambient floor: an unlit cave is black, which is
      DESIGN's intent; the lever if M5d disagrees is one constant in the shader.
- [x] **The fog ignored the light grid, and M5d-1 fixed it — but this entry understated it.**
      Not "the horizon is fog-coloured": Godot's `WorldEnvironment` fog blends the *final* colour
      toward one constant, so against terrain the grid correctly rendered black **the fog was
      100% of what you saw**. The before shot at `--skybrightness=0` is a fully readable
      grey-blue landscape in a world with no light in it at all. See M5d-1.
- [x] **Break-recipe battery: 13 recipes, 9 caught, and both real holes were *joins*.** Nothing
      checked that each corner samples its own vertex (per-vertex light versus per-face light —
      smooth gradient versus flat-shaded facets), and nothing checked that the pipeline hands the
      mesher any light at all: replacing `GatherLight(...)` with `null` at the submission site
      left 556 tests green. **Every part of the path was tested and the wire between them was
      not**, which is the same shape M5c-1 found one layer in. Closed by
      `EachCornerSamplesItsOwnVertexRatherThanOneSharedPoint` and
      `ALampPutsBlockLightIntoADeliveredMeshsVertices`. Suite 556 → 558.
- [x] **Standing habit from it: put a recipe on every *call site* that passes data between
      layers, not only on the logic at each end.**
- [x] **Two uncaught recipes that are not holes.** Shrinking the grid to the plan's 10³ leaves
      everything green, because `Sample` skips zero-weight terms and never forms the out-of-range
      index — so the ring is belt and braces, M4a's contact-epsilon category, and the reason to
      keep it is that **the ring and the zero-weight `continue` are safe only together**: drop
      the ring and deleting that `continue` as tidying walks off the array. And truncating rather
      than rounding in `Scale` differs by at most 1 in 255, below the resolution of anything here.
- [x] **A test whose "not vacuous" guard was the entire test.**
      `AMergedQuadStillHasConstantShadeAlongEveryAxisItMergedOn` first used per-cell random
      light: every face differed, **nothing merged**, and every assertion passed vacuously. Only
      `Assert.True(merged > 0)` failed. Third fixture trap in M5c, from a new direction.

#### M5d — Feel and measurement

**Split M5d-1 (measured, done 2026-08-31) / M5d-2 (played, Matthew's).** The measured half is
headless and repeatable; the played half is the one this milestone was really waiting on, and
nothing in it can be answered without a window and a person.

##### M5d-1 — the measured half — **done 2026-08-31**

- [x] **Quads per chunk: AO off / AO on / AO+light on.** 263 / 493 / 404 per drawn chunk at
      r4h/2v from a standing start, all four arms drawing the identical 165 chunks —
      M5c-2's figures reproduced exactly on a different harness.
- [x] **Mesh memory, and it follows quads including downwards.** 32.8 / 39.1 / 33.5 / 37.9 MiB
      static for the four arms. AO costs +6.3 MiB and adding light *on top of* AO gives back
      1.2 MiB, at ~174 bytes per quad. M2d's "memory is mesh-dominated" survives M5 with the
      shipped arm cheaper than the AO-alone arm in both quads and bytes.
- [x] **Light cost, timed directly, with its denominator.** `ChunkPipeline` exposes
      `LastLightStepMillis` and a derive/propagate split off the same injectable clock the
      budget uses; `light ms` is a profiler series; `cells` prints visits, rate and ns/cell.
      **The two halves deliberately do not sum to the whole** — the leftover is marking relit
      chunks and releasing waiters, and folding it into the walk would inflate the one number
      M5-PLAN already overestimated.
- [x] **Remeshes per swing, and the second half of the fragment-shader trigger is retired.**
      **1.0 relight + 0.5 edit per swing** at r6h/3v and 8 m/s, 87 swings, digging straight down
      from open sky — which is breaking into a lit space five times a second. Against a recorded
      trigger of ~16. **Sixth named fallback dissolved by measurement, none built.**
- [x] **Initial fill from a standing start: 3.4–3.8 s in Debug**, ~1.6 s projected in Release,
      against M5-PLAN's estimated ~0.25 s. Recorded as three moments — first draw, light
      settled, all settled — because M5 moved the first one and the plan estimated the second.
- [x] **`--speed=0` is askable now.** A profile defaulted to 40 m/s whenever speed was zero *or
      unset*, so the standing start the fill measurement needs was the one arm that could not be
      requested. `_speedGiven` is the distinction.
- [x] **`Overburden.Bench --light`**, the same instrument M2d-2 used for caves pointed at the
      other half of the pipeline. Loads `mods/minercraft/` through the real loader, because
      conductance is a function of which materials are opaque and a fixture measures a made-up
      world. **Release: derive 0.241 ms/chunk, `CopyFrom` 59% of it, drain 47 ns/cell.**
- [x] **`frame ms` ignored, and it was worth the warning a fifth time.** The fall profile
      reported `frame ms` max **55.168** against a directly-timed `update ms` max of **9.038**.

- [x] **THE FINDING: light bounds how fast you can move, and the symptom is `drawn` going to
      zero rather than a frame spike.** Light derives on one sim thread against generation's
      eight workers, and a chunk that is not lit does not mesh. At r4h/2v in Debug:

      | speed | drawn | first draw | main thread | derives/tracked |
      |---|---|---|---|---|
      | 6 m/s | 160 | 3.83 s | 35.5% | 2.8x |
      | 12 m/s | 188 | 5.21 s | 60.5% | 4.4x |
      | 20 m/s | **0** | never | 76.6% | 6.0x |
      | 40 m/s | **0** | never | 76.2% | 4.4x |

      **The cliff is 12–20 m/s and the flycam's sprint is 6 × 5 = 30 m/s**, so holding Shift in
      fly mode stops the world drawing in the Debug build `start.cmd` runs. M3d's streaming-only
      control at r6h/3v and 8 m/s was **0.80% of one core**; the same config today is **69.5%**.
      Light on the sim thread is a deliberate M5 decision, so the *kind* of cost is by design —
      its *size* is not, because M5-PLAN costed the propagation and said nothing about the
      derive, and the derive is the whole of this. **~12% in Release**, so the cliff is a Debug
      cliff in a build people play.
- [x] **M4d's headline claim survives, and for a reason worth stating.** The fall profile with
      light in the loop still reports `held 0` and `0 flickers over 120 resting steps` at
      terminal 40 m/s. The collider asks for voxels and the barrier gates meshes, so **light
      bounds what you can see, not what you can stand on.**
- [x] **M5-PLAN's propagation estimate was good and M5c-1 only checked half of why.** M5c-1
      checked the visit *count* (585k against 779k). The other factor — ~20 ns per visit — had
      never been checked: it is **47 ns** in Release, so 15 ms predicted against 25 ms measured,
      1.7x out. **Caveat that invalidates any single ns/cell figure:** the fall profile reports
      50 ns/cell in Debug where the standing start reports 460 in the same build, because a
      falling player propagates mostly full-skylight free fall. **The per-cell cost is a property
      of the mix.** Quote it with its scenario or not at all.
- [x] **`PaddedChunk.CopyFrom` is 59% of the derive** — measured, where M5c-1 estimated 60–75%
      and published no measurement. At the bottom of its own estimate.
- [x] **The fog was not "ignoring the light grid" — it was the brightest thing in a dark
      world.** Godot's `WorldEnvironment` fog blends the *final* colour toward one constant, so
      against terrain the grid correctly rendered black the fog was 100% of what you saw: at
      `--skybrightness=0` the before shot is a fully readable grey-blue landscape in a world
      with no light in it. Fixed with `render_mode fog_disabled` on the voxel material plus our
      own exponential fog fading toward `fog_colour × sky_brightness × COLOR.r` — the fragment's
      own **sky** exposure, because fog is in-scattered daylight and a lamp a metre away does
      not scatter across a room. Daylight is pixel-for-pixel unchanged, verified with a pair.
      - [x] **The environment fog stays enabled and only the material opts out.** Removing
            `fog_enabled` from `Main.tscn` also un-fogs the **sky**, exposing the procedural
            sky's dark ground hemisphere as a black band along the horizon. That is what
            `fog_disabled` being per-material is for. Caught by a screenshot and nothing else.
- [x] **One test, and it was watched failing.** `TheLightStepsTwoHalvesAreContainedInItsWhole`
      pins that the three brackets enclose the regions they claim to, under a fake clock
      advancing a millisecond per read — an invariant rather than a duration, so it holds under
      a real clock too. Broken by "time the drain from `entered` rather than `derived`", which
      double-counts the derive. Carries the loop-ran companion assertion.

##### M5d-2 — the played half — **mostly answered 2026-08-31**

Played with the entangled miner. **"Looks good to me."**

- [x] **Does a mine read as a mine? Yes.** One chamber runs from lit rock to *actual black*
      across its width and the falloff reads as depth; another shot is near-total darkness with
      a few faces catching a little light. **Darkness is real** — the thing M5 exists to supply
      and M13 turns into a hazard, and the first time in this project that a screenshot of
      nothing is a pass. **The fog fix is load-bearing for this**: before it, that black was the
      fog's grey-blue and the gradient was distance rather than light.
- [x] **Does AO earn its quads? Yes.** Visible on every block edge and corner, and it is what
      makes the excavation read as sculpted rather than as a pile of cubes. DESIGN's "single
      biggest visual payoff" was a claim and is now a verdict.
- [x] **Are the face-tint constants right now the sun is gone? Yes — not raised.** Top, side and
      bottom faces stay distinguishable in the dark, which is the job they took over from the
      directional light. They live in the shader and not the merge key, so a retune whenever it
      comes costs no remesh.
- [x] **Does rock beside a lit tunnel glow? Not raised.** The transition to black reads clean —
      the known cost of a per-cell value, apparently mitigated enough by the half-cell normal
      offset.
- [x] **Is 1 m too coarse down a `hand_pick` tunnel? Closed by default — 1 m stands.** Lots of
      play across lit, dark and torchlit chambers ending in "happy with the current state", with
      light quality never raised. **But the specific play was not done**: the entangled miner
      digs chambers, and a chamber is many cells wide, which is the case the grid is good at. So
      **1 m stands because nobody reopened it, not because the narrow case was probed and
      passed** — silence rather than evidence, the same shape as M4d's unjudged step-up
      wall-hug, and worth the distinction because "tested and fine" and "never provoked" decay
      differently.
      - [ ] **The trigger therefore stays live** rather than being retired, unlike the
            fragment-shader fallback which was retired properly in both halves. A 0.75 m bore
            that reads blobby reopens it; 0.5 m cells then cost 4 KB per chunk and 8× the
            propagation — **and since M5d-1 the propagation is the cheap half**, so the real bill
            is the derive, which is already the thing under pressure.
- [x] **Can you mine by torchlight? Yes, and it is warm.** One lamp underground lights the
      chamber in a distinctly yellow cast. **This is the only test in the milestone that could
      have proved the two channels are independent all the way to the screen** — M5c-2 recorded
      that the merge key, the trilinear sample, the quad counts and the shader would all pass
      identically with both channels packed into one byte. It is also the receipt for the
      two-channel decision M5a paid for in vertex bytes, since DESIGN's M7 wet-surface term needs
      skylight on its own.
      - [x] **A second shot went one better: one lamp in a *skylit* cavern, both channels in the
            same frame.** Cool grey from `COLOR.r` through `sky_tint`, a golden pool from
            `COLOR.g` through `block_tint`, and a clean boundary between them. The first shot
            proved block light exists and is warm; **this one proves the two channels do not
            contaminate each other** — the additive composite blends two differently-coloured
            fields into one lit space rather than a muddy average. The picture to point at when
            somebody asks why they were not packed into one byte.
      - [ ] **Observation, not a defect: 15 m of light is *sixty* voxels.** `lightEmission: 15`
            and 1 per 1 m cell, in a game whose corridors are three to five voxels wide — so one
            lamp lights a whole working, and the relative reach is 4× what a 1 m-block game gets
            from the same number. **The same trap the cell size already carries**, one level
            along: TODO calls 0..15 "a game-balance number wearing a storage costume" but never
            said what it looks like in voxels. **The lever is one number in one JSON file with no
            rebuild** — `lightEmission` in `lamp.json`, the same shape as `radiusMetres` for the
            bore. Halving to 8 gives a 32-voxel pool. Left at 15 because it looks good and
            nobody has complained.
- [ ] **Is the third shot's cavern legitimately lit, or is skylight leaking?** A large cavern
      lit fairly evenly with one small sky opening visible. Skylight falls free straight down but
      attenuates 1 per metre horizontally, so one hole should light ~15 m and no more. Either the
      ceiling is thin and perforated out of frame — legitimate — or the cell-open-to-sky test or
      conductance is leaking. **The other two shots argue against a leak**, since an enclosed
      space in the same session went properly black.
- [ ] Can you mine by torchlight?
- [ ] **New: does the 12–20 m/s cliff read as a bug at the flycam's normal 6 m/s?** The numbers
      say 35% of one core and a 3.8 s fill there. Whether that is *felt* is a different question
      from whether it is measurable, and it decides how urgent the two levers below are.
- [ ] **Still unjudged from M4d, and ordinary play cannot reach them:** step-up guard 3's
      diagonal wall-hug, and step-down's 0.5 m instant drop.
- [ ] **Still unjudged from M3d/M4d: Ctrl vs Alt for precision mining.** Alt is the better key —
      free in walk, *crawl* in fly, so "slow down to do fine work" pairs rather than fights —
      and the swap is one line. Left on Ctrl pending exactly this play-test.

##### The entangled miner — built 2026-08-31, as a debug bind

- [x] **Point at anything, hold left click, it eats what it is pointed at however far away.**
      **20 swings a second** (4 → 8 on 2026-08-31, 8 → 20 on 2026-09-01, both at Matthew's
      request), **Q** toggles it, no visual effect.
      - [x] **Re-measured at every rate rather than assumed, three times now, and the measurement
            keeps saying the same surprising thing: faster is cheaper per swing.** 0.139 ms at
            4/s, 0.072 at 8/s, **0.070 at 20/s** — consecutive swings chew the previous bite, so
            most of the sphere is already air. **What rises is the remesh load, which is per swing
            rather than per voxel**: 32.3 edit remeshes a second against 13.9, `update ms` mean
            3.651 against 3.245 with its max *lower*. **That is the number to watch if it is
            raised again**, not the swing cost.
      - [x] **The ceiling has a name: one swing per frame, at 0.0167 s.** Below it the cooldown
            stops pacing the tool and the frame rate becomes the balance, so the dig rate would
            depend on the machine. 0.05 s leaves a factor of three, and a 60 s run already lands
            at **~18.9 swings/s rather than 20** because a swing can only fire on a frame
            boundary — the ceiling showing harmlessly.
      Built as an instrument for M5d-2:
      every question in that list is about what a *large* excavation looks like, and digging one
      by hand at 5 m of reach is why they are still open.
- [x] **Deliberately not a tool tier, and that is the whole design decision.** *Reach is the
      player's, not the tool's* is locked twice (M3d, restated M4c) on the grounds that putting
      it in the tool JSON would "guess a field into a format mods have written against". A tool
      whose defining property is unlimited reach is the case that decision did not anticipate,
      and for this one tool the reversal is arguably right — the reach really is a property of
      the mechanism rather than the arm. So: a mode on `MiningController`, no `reachMetres` in
      the JSON, no `ToolDefinition` field, same spirit as the L lamp bind.
- [ ] **Decide before it becomes real content: does `ToolDefinition` gain an optional reach?**
      Once that field exists every mod writes against it and the locked decision is gone for
      *every* tool rather than for this one. Wants its own planning pass. Until then the
      entangled miner stays a debug bind and stays obviously not-the-feature.
- [x] **"However far away" means "as far as the world exists", ~144 m at r16.** What stops the
      ray is `RayOutcome.Blocked` at the edge of the loaded region — M3b's Blocked-is-not-Missed
      doing its job. Entanglement is not distance-sensitive but **the streamer is**. The 512 m
      constant is a backstop bounding the DDA, the same shape as `MaxRadiusMetres` bounding the
      carve. Aiming at sky costs no swing and no cooldown.
- [x] **Composes with the number keys.** It changes how far and how fast, never how big a bite
      or what the bite is hard enough to take — both stay the tier's, which keeps `Carve.Take`'s
      hardness gate the only gate. `radiusMetres` in the tool JSON is still the one-number lever
      for a wider bore, unchanged since M4d named it.
- [x] **Measured, because a bind nobody has run is a bind that does not work.** `--entangle` is
      the sixth `--` seam of this shape; with `--mine` it aims along the flight path rather than
      down, which is the one thing that needed the flag. **69 swings, 11.1 m mean and 18.9 m
      furthest reach against the player's 5 m** — terrain-limited, not reach-limited. Bite
      unchanged at 22 voxels, `0 too hard`, `clipped 0`.
      - [x] **`MaxReachMetres` exists only because nothing else could see this.** Swing count and
            voxel count are identical whether the cast ran to 5 m or 90 — the M5c-2 shape of
            hole, where every layer is tested and the wire between them is not.
      - [x] **The longer cast costs 0.139 ms a swing against 0.083** — 1.7×, two orders of
            magnitude inside a frame. **Relight remeshes are 0.9 per swing against 1.0**, so
            carving a hundred metres away is not a different cost from carving at arm's length.
- [x] **The rate is fixed at 8/s, and raising it from 4 inverted a recorded caveat.** At 4/s
      entangled was marginally *slower* than `steel_pick`'s own 0.20 s and the only upgrade was
      the range; at 8/s it beats every shipped tier, so **the tier's cooldown no longer bounds
      anything while entangled**. Nothing shipped is balanced by its rate today — the note is for
      when something is. Fixed rather than the tier's because otherwise the dig rate depends on
      which number key was pressed last, and this exists to excavate a *comparable* test volume.
      - [x] **Doubling the rate cost nothing measurable**: 134 swings against 69, `swing ms` mean
            **0.072 against 0.139**, remesh per swing unchanged at 0.7, main thread 75.9% against
            76.5%. Swings got *cheaper* because consecutive ones chew the previous bite — 13
            already-air voxels per swing against 11.
      - [x] **Relight remeshes per swing halved, 0.9 → 0.5**, same mechanism: consecutive swings
            repair light a previous swing already repaired. So M5d-1's 1.0 per swing is an upper
            bound that *improves* under sustained digging, which is the direction that matters,
            since the trigger it retired was about a rate mining can sustain.
- [x] **Q rather than F12**, asked for and agreed: free (WASD leaves QE alone), mnemonic, and
      under the left hand for a toggle used mid-dig. F12 is the Steam overlay's screenshot key.
- [x] **Refused during a hand-toggled profile run**, the same as walk mode, because `--mine` is
      pinned to the un-entangled sphere so M3d's baseline stays comparable. The report says
      outright when a run is not comparable.

##### Local lighting flicker on a chunk boundary — recorded, deliberately not chased

- [ ] **Symptom and repro, in Matthew's words:** *"I could get local lighting to flicker by being
      on a boundary and rotating back and forth on a specific angle, but it was negligible."*
      Stand on a chunk boundary, rotate the view back and forth through one particular angle,
      and nearby lighting flickers. Seen once during M5d-2's play; play continued and ended
      "happy with the current state".
- [x] **Decided 2026-08-31: do not chase it.** Not because it is not real, but because
      **the fix would mean complicating the lighting maths**, and this milestone is already
      balancing a 1 m grid against 0.25 m voxels, two channels in a merge key, a sim-thread
      budget, and a derive cost that M5d-1 found to be the binding constraint. **A negligible
      artefact is not worth adding a term to a system that is already the most delicately
      balanced thing in the project.** Recorded rather than fixed, which is the whole point of
      recording it.
- [x] **One hypothesis is already eliminated, and it is the obvious one.** *"The viewer chunk
      oscillates at the boundary, so chunks at the far edge unload and reload and their light is
      rebuilt."* **It cannot be that**: `FlyCam`'s mouse-look assigns `Rotation` only and never
      touches `Position`, and `ViewerChunk()` reads `GlobalPosition` — so **rotating cannot move
      the viewer between chunks**, and nothing in the streaming or relight path is reachable by
      rotation alone. Worth having written down, because it is the first thing anyone will
      assume and it would cost a session to rule out from scratch.
- [ ] **What that leaves is render-side**, since neither the light grid nor the camera position
      changes when you rotate. Candidates, in rough order of likelihood, for whoever picks it up:
      - **Coincident or near-coincident faces across the boundary**, where the depth test
        resolves differently by angle — reads as flicker, would actually be *geometry*, and only
        looks like a lighting bug because adjacent faces carry different shades. A mesh built
        from a padded copy that disagreed with its neighbour at the moment it was built is how
        two chunks end up both emitting a face on the shared plane.
      - **Frustum culling** popping a chunk in and out at the exact angle where its bounds cross
        the frustum plane.
      - Anything that makes one chunk's mesh momentarily stale relative to its neighbour's.
      - `--skybrightness=0` isolates the channel: an artefact that survives it is block-light or
        geometry rather than skylight.
- [ ] **Revisit trigger: when it stops being negligible.** Specifically — if it becomes visible
      without deliberately provoking it, or if anyone reports a dark patch that does not go away.
      That second one is the classic voxel bug **M5-PLAN opens the whole milestone by naming** —
      *"a dark patch that nobody can explain, does not go away, and only fixes itself when the
      chunk happens to unload and come back"* — so this sighting is filed in the right family
      even though it is currently harmless. The repro above is the expensive half of any future
      investigation and it already exists.
- [ ] **Separately, and not this: a tunnel that partly vanished is not a bug.** An edited chunk
      that unloads loses its edits and regenerates from seed. Known since M3, and **M6's SQLite
      save/load is the fix.**

##### Rotating changes how bright the room is — sighted 2026-09-01, diagnosed and fixed at M7c-1

> **Answered 2026-09-02: it was the view-space normal, the second of the two explanations below.** The wet sheen is what forced it; the fix and its measurement are at the bottom of this section. Kept whole because the two-explanations pass is the reasoning, and because *which look is preferred* is still Matthew's and still open.

- [ ] **Symptom, in Matthew's words:** *"if i turn slightly left it gets brighter... its a minor
      cosmetic and i kind like it now anyways."* Two screenshots of the same sealed room a few
      degrees of yaw apart, the second visibly brighter overall. **Not chased, at his instruction**
      — and the second half of that sentence is why this is recorded as *a question about intent*
      rather than as a defect awaiting repair: **he likes it.** A camera-relative shade is a look
      some voxel games choose on purpose.
- [ ] **Two explanations fit those frames and they are not the same event.**
      - **Benign composition.** `face_tint` gives ±X walls 0.65 and ±Z walls 0.8, so two adjacent
        walls of a room differ by 23% *by design*. Turning changes how much of each fills the
        frame, so the average brightness of the picture moves while every face keeps its own
        shade. Nothing is wrong; the constant is doing exactly what its comment says.
      - **A view-space normal, which would be a one-line defect. ← this one.** In a Godot 4 spatial shader
        `NORMAL` inside `fragment()` is in **view space** — the same convention `Voxel.gdshader`
        already notes for `VERTEX` two comments further down. `face_tint(NORMAL)` would then be
        classifying a face by its orientation *relative to the camera*: yaw carries a wall's
        normal across the `abs(n.x) > 0.5` threshold and the wall itself steps between 0.65
        and 0.8.
- [ ] **The discriminating test takes two seconds and needs no instrument: point at one wall and
      pitch up and down.** Under composition a single wall's own brightness cannot change. Under a
      view-space normal, pitching far enough gives a vertical wall `n.y > 0.5` and it renders at
      the **top-face** value of 1.0 — a wall lighting up like a floor.
- [ ] **If it is the view-space normal it is a better candidate for the flicker above than any of
      the three recorded there**, and the same family: rotation changing what is drawn while
      neither the light grid nor the viewer moves. That list is coincident faces, frustum culling
      and staleness; none of them is this, and this one is **reachable by rotation alone**, which
      is the property that repro turns on.
- [x] **Settled at M7c-1 (2026-09-02): it was the view-space normal, and it is fixed.** The wet
      sheen is the second view-dependent term M7-PLAN said would turn this from a look preference
      into a correctness question, and it did — with both terms sliding there is no way to tell the
      one that is supposed to move from the one that is not.
      - [x] **The predicted one-line fix was not what shipped, and the shipped one is cheaper.**
            `face_tint((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz)` is a matrix multiply per
            fragment; a `varying` set from `vertex()`'s `NORMAL` is exact and free, because a chunk
            instance's transform is a pure translation (`Basis.Identity` — see `Main.OnMeshReady`),
            so the model normal **is** the world normal. **If a chunk ever gains a rotation this
            becomes `MODEL_NORMAL_MATRIX * NORMAL`**, and the shader says so.
      - [x] Measured rather than argued: `riser face` (69,130,64) in both arms at yaw 0,
            (59,128,54) against (53,116,50) at yaw 40 — **identical facing the default direction,
            diverging once you turn**, which is the defect stated as a number.
      - [ ] **Still an A/B for a person, as this item always said.** Correctness settled and world
            space shipped; `--viewnormal=1` restores the old arm and `--yaw=` makes the two
            comparable.

##### Surfaced by M5d-1, not built — each wants its own planning pass

- [ ] **Lever 1: `PaddedChunk.CopyFrom`, 59% of the derive** (measured). It recomputes the
      neighbour index per voxel where 27 block copies would do, and it would speed up **meshing**
      too, since a mesh job builds one per chunk as well. Ceiling ~1.7×. Named at M5c-1 and
      deliberately not built there on the grounds that raising the budget bought back the whole
      regression — **the budget cannot buy this one back**, because at 20 m/s the work exceeds
      the frame however it is sliced.
- [ ] **Lever 2: re-derives, 2.4–6.0 per chunk against 1.0 at a standing start.** A chunk
      re-derives every time a neighbour appears where it had assumed air. M5c-1 wrote the trigger
      in advance — *"a ratio that climbs is the signal that `LightNeighbours`'s gain-only rule has
      stopped being enough"* — and it climbs monotonically with speed. Ceiling ~2.4×, **larger
      than the copy, and not where M5c-1's attention was pointing.**
- [ ] Together ~4×, which would put the Debug game near 17% of one core at 8 m/s and move the
      cliff past the flycam's 30 m/s sprint.
- [ ] **`screenshot.gd`'s 8 s settle was tuned before light existed.** At r10 nothing has drawn
      by then and the shot comes back empty sky. Either raise it or have it wait on
      `IsQuiescent` rather than on a clock.

#### Decisions, all four settled 2026-08-30 before any code

- [x] **Light cell size: 1 m. DESIGN's number stands.** The `--lightcell` A/B flag is therefore
      **not built** — it existed to answer a question and the question is answered. **Revisit
      trigger:** M5d plays a `hand_pick` tunnel and the light reads blobby across a bore narrower
      than one cell; 0.5 m then costs 4 KB per chunk against 512 bytes and 8× the propagation,
      and `LightCellSize` already permits it. Worth restating why it was a question: DESIGN
      reasons in **metres** — "light is low-frequency, nobody sees the difference, 64× cheaper" —
      and the number that decides whether it *looks* right is in **voxels**. A 1 m cell is 64
      voxels and the tunnels are 0.75–1.25 m across, so the cell is wider than the corridor.
      That is why the trigger is a play rather than a number.
- [x] **`SkyColumn` is M5b**, with its lifetime in M5c. See the split recorded under M5b.
- [x] **The directional light goes.** The grid is the light. See M5c.
- [x] **Emission range 0..15**, matching the field with no scaling. **Revisit trigger, in
      Matthew's words: "unless we come up with better lighting later"** — a wider field or
      coloured light would take the range with it. Recorded because 0..15 means the brightest
      lamp any mod can define reaches exactly 15 m, and it reaches it for a *storage* reason
      rather than a design one: a **game-balance number wearing a storage costume**, the same
      shape as terminal velocity being a streaming parameter in a physics costume.
- [x] **One of the four moved work earlier rather than removing it.** Settling the directional
      light meant the grid is the only light, and DESIGN's M7 wet-surface term needs skylight on
      its own — so the vertex carries two channels rather than one pre-multiplied value, and
      that lands in **M5a**, not M5c. See M5a.

#### Surfaced elsewhere

- [ ] **`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` is intermittent.**
      It failed in 2 of M5b's 12 break-recipe runs and passes on a clean tree. M2a's, not M5's —
      but **a flaky test is worse than a missing one during a mutation battery**, because
      "1 failed" reads as "the recipe was caught" and it took reading the failure names to see
      that no lighting test had failed at all. Worth pinning down before the next battery.

#### Deliberately not in M5

- [ ] Coloured light — three channels, three BFS passes, 12 bits per corner in the merge key.
      Nothing in DESIGN asks for it.
- [ ] Directional or cone lights. A headlamp is a shader effect, not a propagated field, and it
      would be M10's machine content anyway.
- [ ] Attenuation through translucent materials — water is the only candidate and it is M8. The
      conductance derivation is the right place and the predicate is already shaped for it.
- [ ] Placement of light sources through a real UI — M6.
- [ ] Persisting light. It is derived data: 512 bytes recomputed in microseconds against a chunk
      blob of kilobytes. M6 persists *edits* and the light follows. **`SkyColumn` is the
      exception** and is genuinely edit-derived, so it goes on M6's list.
- [ ] Distance LOD interacting with lighting — M13, and the coarse grid is already most of the
      answer: a 1 m light field downsamples where a voxel field does not.
- [ ] Darkness as a hazard — M13. M5 supplies the darkness and nothing that punishes it.
- [ ] Baking AO into the light grid. Different frequencies — AO is a 0.25 m effect and the grid
      is 1 m — and merging them loses exactly the detail AO exists to add.

### M6 — v0.1 playable sandbox ← **all four parts built, and played 2026-09-01. Every scheduled
item is ticked; what is left was surfaced by the play rather than scheduled.**

Planned into four parts in [docs/M6-PLAN.md](docs/M6-PLAN.md). **Read it before starting.** The
structural point: every chunk so far has been recoverable from nothing, so `Forget` could delete
a record inside a thousand-chunk sweep and nothing had to think about it. **M6 is the first
milestone that creates state whose only copy is on disk, which means the unload path becomes a
write** — and a persistence bug destroys work that cannot be recreated, usually a session later,
by which time the evidence is gone. This is the first milestone where the tests are the only
thing between a defect and permanent data loss.

The second structural change: **a chunk gains a third possible source of voxels.** Memory, then
disk, then the seed — and the only failure that matters is consulting them in the wrong order,
because regenerating a chunk that had a blob is a silent, total loss of everything in it.

#### The three decisions that set the shape, all settled in the plan before code

- [x] **The store is an interface in Core; SQLite lives in a new `Overburden.Persistence`
      assembly.** Core has **no NuGet dependency at all** after five milestones, which is what
      makes non-negotiable 1's multiplayer seam more than a slogan.
      `Microsoft.Data.Sqlite` would be its first and carries native binaries.
      `IChunkStore` + an in-memory implementation is the same shape as M1's `IContentSource` /
      `DirectoryContentSource` / `InMemoryContentSource`, built then for the same stated reason.
      **`ChunkPipeline` takes an `IChunkStore?` and null means no persistence**, so all 559
      existing tests keep passing unedited. **No ADR** — DESIGN already locks SQLite over region
      files; this is where it lands, not whether.
      - [x] **Probed 2026-08-31, so M6b's first step is not a risk.** `Microsoft.Data.Sqlite`
            **10.0.11** restores against a `net8.0` project under the 10.0.302 SDK and is now in
            the local NuGet cache, so **M6b needs no network**. The native `e_sqlite3` loads
            (SQLite **3.53.3**), a 16 KB blob round-trips byte for byte, and `journal_mode=WAL`
            engages. `SQLitePCLRaw.bundle_e_sqlite3` does come along as predicted, so the Godot
            export question is real and still unanswered — **that is the part to check, not the
            restore.** Pin the version explicitly rather than taking the 10.x default against a
            net8.0 target.
      - [ ] **In WAL mode the main `.db` file stays at 4,096 bytes until a checkpoint** — the
            probe wrote 16 KB and the file did not grow, because the data was in the `-wal`
            sidecar. Worth knowing before an hour is spent on "my saves are not being written".
- [x] **Saving is a job on the serial lane** — M2a built that lane for this and said so, and the
      flag has been `false` at every submission site since. **The argument that put light on the
      sim thread does not transfer**: a blob is a pure function of one chunk, which is
      `MeshChunkJob`'s shape exactly, and `fsync` on the sim thread is the textbook frame hitch.
- [x] **The ID table lives in the same database as the chunks, in the same transaction.** The
      obvious layout — JSON world file beside a SQLite chunk DB — is the one arrangement that can
      desynchronise, leaving blobs whose numeric IDs mean something a version out of date.

#### Two exceptions to pipeline-wide rules, and both lose data if missed

- [x] **A save job must set `VoxelsSharedWithJob`**, as `Neighbourhood()` does for all 27 of a
      mesh job's. Not because a chunk being unloaded is normally written to, but because it can
      be **revived and then mined while the serialiser is mid-copy** — and M3a records that this
      is not a stale voxel but `VoxelChunk.Set` swapping the backing arrays under a reader.
- [x] **`OnChunkUnviewed` must not cancel it and `Dispose` must not drop it.** Every other job is
      cancelled the moment nobody wants its chunk, which is right for work we can rebuild. A
      cancelled save is a deleted tunnel. `JobResult`'s contract says a job still queued at
      shutdown reports Dropped, so **quitting with saves queued loses them** — the shutdown path
      has to drain the serial lane. **The single most likely way to ship a data-loss bug in M6,
      and it is one line of `Dispose`.**
- [x] **`DirtySinceSave` is a second flag, and it is the `NeedsRemesh` problem a third time.**
      `HasEdits` keeps meaning "differs from the seed" and is never cleared; `DirtySinceSave`
      means "differs from what is on disk", is cleared **at submission** (because `SetVoxel` will
      set it again if an edit lands during the job), and must be **restored by `StepBack` if the
      job drops or faults**. Conflating them loses exactly the last edits before an unload —
      the most recent work, and only sometimes.

#### M6a — The blob and the world file ✅ **done 2026-08-31, 614 tests passing**

`Overburden.Core/Persistence/` — `ChunkBlob`, `SkyColumnBlob`, `WorldHeader`, `BlobFormat`.
Suite 559 → 614. Nineteen break-recipes, eighteen caught outright; the two that were not
straightforward are written up below because neither was a plain pass.

- [x] `VoxelChunk` ⇄ bytes across all three storage kinds. Pure Core, no SQLite, no file.
- [x] **A version byte first, and an unknown version refused rather than read.** Everything else
      in M6 is rewritable in an afternoon; a format is not, once anybody has a saved world — and
      the person who has one first is Matthew, mid-milestone.
- [x] **The palette is written as world numeric IDs, not per-chunk indices.** That mistake
      round-trips perfectly within one session and the type system cannot prevent it, so it goes
      on the break-recipe list.
- [x] **zlib, BCL only, so not a dependency — and zlib rather than raw deflate, which a truncation
      test is what said.** Raw deflate carries **no integrity check at all**: cut a blob after the
      last byte the decompressor needed — the four-byte Adler and the byte holding the
      end-of-stream marker — and it inflates to exactly the declared length and reports a clean
      end, so every check passes. Removing *fewer* bytes is caught. **Non-monotonic in the
      truncation length, which is why the exhaustive loop found it and a spot check would not.**
- [x] **The frame declares the stored length as well as the uncompressed one.** That is what
      actually catches truncation, before decompression starts. The two mechanisms are
      complementary: the length catches truncation, the Adler-32 catches bit-rot inside an intact
      blob. `CorruptionInsideAnIntactBlobIsRefused` exists because with the length check in place
      nothing else would have failed and the six bytes would have been decorative.
- [x] **Measured, and the plan was 5× pessimistic: 189 B per varied chunk**, so 185 KiB per 1,000
      stored chunks and **18 MiB per 100,000**. Compression 22–26×. The plan estimated ~1 KB a
      chunk. **So the save file really is not the thing to be nervous about** — see the read-side
      number now recorded under M6b.
- [x] **`Compact()` is measured, and it does not run on serialisation. The M1 comment is
      corrected.** On a carved chunk — the typical save — it costs **13× the entire write and
      saves nothing**, because carving air into a chunk that already holds air leaves the palette
      exactly as it was. On the case it exists for, a chunk that has lost the last voxel of some
      material, **10× the write for 7.5%** of a blob already compressed 22×: about 100 µs for five
      bytes. `ChunkBlob.Write` defaults to `compact: false`; the flag stays as the bench's A/B arm.
      - [x] **The first measurement was wrong and the fixture was the trap.** The edit pass carved
            spheres of air, which cannot create a dead palette entry, so the bench reported
            "saves 0" from a fixture that could not have saved anything. The `stripped` arm now
            removes a material outright from 644 of 2,048 chunks. **Same family as M5c-1's
            one-voxel bore: the scenario made both arms agree and the numbers could not show it.**
      - [ ] **Revisit trigger on `Compact`:** a chunk edited across enough sessions to accumulate
            dead palette entries and widen its packing. The palette never shrinks on its own and
            256 entries converts a chunk to 64 KB of direct storage. Unreachable with five shipped
            materials; a heavily modded world is where it would first bite.
- [x] **`Compact` reorders the palette into scan order**, so air moves from entry 0 to last. The
      observable difference between the two arms, and now pinned — it is what gives "the oracle is
      the voxels, not the representation" teeth rather than leaving it a slogan.
- [x] **`SkyColumn` ⇄ bytes, and the argument is not size.** 2,048 B a column. A column's open
      height is the topmost opaque voxel **across all Y**, so re-deriving it needs chunks the
      2–3 chunk vertical radius does not load — **the only derived data here that genuinely
      cannot be re-derived**. Skipped, a reloaded shaft seeds from the *pre-dig* surface, every
      cell fails `IsCellOpenToSky`, and the shaft is dark for a reason nothing on screen explains.
      **Loading a chunk without its column is worse than loading neither.**
- [x] **A column needs its own dirty bit**, the mirror of `HasEdits`. Built, and **set inside
      `OnVoxelChanged` rather than by its caller** — a caller that forgets loses a shaft's
      lighting with nothing failing to say so. A restored column comes back already marked,
      because coming back pristine would drop it unwritten on the next unload and lose the
      shaft on the *second* visit rather than the first. `OnVoxelChanged` returning
      true is the exact test — it returns true only when the height actually moved, so a column
      dug below its top is unchanged from its analytic seed and correctly not written.
- [ ] M6b: **the column save hook is `ReleaseColumn`, not `Forget`** — two hooks on two lifetimes,
      because a column is refcounted on (x, z) and survives the vertical unload.
- [x] Refuse, don't repair: unknown version, truncated blob, **material ID past `MaterialTable`'s
      `Count`** (M3c's recorded item, *"the fix is validating chunk contents at load: M6"*), and
      **an ID table whose entry 0 is not `minercraft:air`** — non-negotiable 3 is a constant the
      whole engine assumes, and a world file where it is false makes it a lie.
- [x] **Oracle: round-trip identity, voxel for voxel — not representation for representation.** A
      compacted chunk may legally come back uniform, and asserting on `Kind` would fail on working
      code (M3b's shape, an oracle wrong in the subject's favour). **Its limit: the same writer
      and reader agree with themselves, so a symmetric offset bug passes.** The complement is a
      hand-constructed blob asserted byte for byte, one per storage kind.

##### What M6a settled that M6b should read first

- [x] **Non-negotiable 2 is tested through bytes for the first time in five milestones.**
      `ExportIdTable` / `SeedIdTable` were built at M1 and had never had a caller; the whole loop
      now runs through serialised bytes — save with a mod, remove it, reopen from the bytes alone,
      reinstall, reopen, exactly as it was.
- [x] **Non-negotiable 3 gets a guard: entry 0 must be `minercraft:air`, refused at read *and* at
      write.** Refusing at write too, because a table that wrong reaching disk is a bug upstream,
      and catching it there names the session that produced it rather than the one that later
      failed to open the world.
- [x] **`WorldGenSettings` is thirteen properties, not a seed, and all thirteen are written.** An
      unedited chunk is not stored but *regenerated* from all of them; change the default
      `CaveLatticeStep` in a later build and the unstored terrain around a saved base comes back a
      different shape. **Explicit binary rather than JSON, and the failure mode is the reason** — a
      JSON reader given a missing field fills in the default and carries on, which is exactly that
      silent reshaping. A reflection test counts the record's properties against the constant the
      serialiser was written to, because adding a fourteenth field and forgetting it would leave
      the round trip **green**: record equality would compare a default against a default.
- [x] **Blobs carry their own position and it is checked on read**, so a blob filed under the wrong
      key is a loud refusal rather than terrain appearing somewhere it does not belong.
- [x] **The battery's one green recipe is belt-and-braces, not a hole.** `ChunkBlob.ReadPalettised`
      validates the packing width before calling `VoxelChunk.FromPalettised`, so the factory's own
      check is unreachable through the blob path — M4a's zeroable-epsilon category. **Both kept**,
      because a validating factory for untrusted bytes must not depend on its caller, and it is not
      decorative: `SetWidth(3)` computes `TrailingZeroCount(3) = 0` and a value mask of 7, a
      silently wrong packing rather than a crash. Four tests now reach the factory directly, which
      turns an unreachable duplicate into a tested one.
- [x] **One battery recipe was mis-designed and read as a pass.** Swapping only the *reader* to raw
      deflate fails 17 tests — but that proves the halves agree, not that the checksum does
      anything, since `DeflateStream` cannot read a zlib header at all. Swapping **both** sides is
      the honest recipe; re-run that way it fails 5, including the corruption test.

#### M6b — The store, the lifetime, and the load path ✅ **done and played 2026-08-31**

**Split three ways, on the grounds M2c and M5c were.** M6b-1 is the store, verifiable alone with
no threads and no pipeline. M6b-2 is the lifetime — the save job, the flags, the load path, the
column restore and the races. M6b-3 is the session — the startup reorder, autosave and the quit
drain. Splitting up front rather than halfway through is why M2c went smoothly.

##### M6b-1 — The store ✅ **done 2026-08-31, 650 tests passing**

- [x] **`IWorldStore`, not `IChunkStore` — a one-line correction to the plan.** It owns three
      things: chunk blobs, sky columns, and the world header carrying the ID table. A store that
      holds the material table is not a chunk store.
- [x] **It deals in bytes, not `VoxelChunk`s**, so `SqliteWorldStore` knows nothing about voxels,
      palettes or light — which is what lets it live in its own assembly at all.
- [x] **`Overburden.Persistence`, a new assembly**, with `Microsoft.Data.Sqlite` **pinned at
      10.0.11** rather than floating. Core still has no NuGet dependency at all.
- [x] **Chunks and columns write in one call, in one transaction, and that is a correctness
      requirement.** A shaft's chunks stored without its column reload with the column seeded
      analytically — from the *pre-dig* surface — so every cell fails `IsCellOpenToSky` and the
      shaft is dark for a reason nothing on screen explains. **Two calls would make that state
      reachable by a crash between them.**
- [x] **The header is written at open, before any chunk, and the ordering replaces a transaction.**
      The plan asked for the ID table to commit "in the same transaction as the blob that first
      used it"; it does not need to, because numeric IDs are only allocated during content load,
      which happens once at open.
- [x] **Not thread-safe, as a contract.** One connection, prepared statements reused for the
      store's lifetime. M2a's serial lane is what makes that safe — and the reuse is most of why a
      read is 6 µs rather than statement preparation.
      - [x] **M6b-3 found the one caller that is not on the lane**, `SaveAll`, and made it wait for
            the lane before touching the store. The premise "every call arrives on the serial lane"
            was true when it was written and stopped being true one part later, in the same
            milestone.
- [x] WAL, `synchronous = NORMAL`, `STRICT` tables, and a schema `user_version` **distinct from any
      blob's format version** — they change for unrelated reasons. A newer schema is refused.
- [x] **One contract suite, run against both implementations.** `InMemoryWorldStore` exists so
      M6b-2's pipeline tests need no disk, and **that is exactly what makes it dangerous**: M1's
      `InMemoryContentSource` drifted from the shipped content and nothing said so until M3c wrote
      `ShippedContentTests`. A store that drifts would quietly invalidate the tests standing
      between a defect and permanent data loss.
      - [x] `AStoredBlobIsNotAliasedToTheCallersArray` — the specific way a fake diverges: handing
            back the caller's own array passes every round-trip test while letting a later edit
            rewrite history.
      - [x] `SurvivesAReopen` **says out loud that the in-memory store cannot**, rather than
            skipping and reading as coverage.
      - [x] **Negative coordinates are distinct keys.** Half the world has them, and a key packed
            with a bad shift collapses one chunk onto another — terrain appearing where it does not
            belong, a long way from the cause.

##### M6b-2 — The lifetime ✅ **done 2026-08-31, 668 tests passing**

**The milestone's sentence is now a passing test:** dig a tunnel, fly far enough that the chunk
unloads, come back, and the tunnel is still there. Plus the half a moving viewer cannot reach —
tear the pipeline down and build a new one on the same store, which is quitting and restarting.

- [x] `ChunkPipeline` takes an `IWorldStore?`; save on unview, load before generate, the in-memory
      index, the column restore, `SaveAll`, and a synchronous save in `Dispose`.
- [x] **Defect found by the tests: `SkyColumn` needed the two-flag split and did not have it.**
      Chunks got `HasEdits`/`DirtySinceSave` deliberately and argued; columns were then gated on
      `HasEdits` alone, which is permanent — so every save would rewrite every edited column for
      the life of the world. **The lesson is the location, not the bug:** the conflation was
      reasoned about carefully in one file and reintroduced one file away, in the half nobody was
      looking at. Third outing for this shape, and the first *inside* the change that was fixing it.
- [x] **Two tests were asserting on states that last less than a frame**, both the same way:
      `ApplySave` runs inside `ApplyResults` and `SubmitSaves` runs later in the *same* `Update`.
      Replaced by end states — a store that fails and then recovers ends up holding the data.
- [x] **A fixture trap cost four failures.** The tests reached for voxel (16, 16, 16), which is
      often *already air*, so `SetVoxel` correctly early-outed and no edit was made. Fixed by
      placing a lamp — nothing generates one — and by having the fixture itself assert the chunk
      came out dirty, so the next fixture that stops editing fails loudly instead of going vacuous.
- [x] **Incidental, a part early: placing an opaque block above the surface closes the sky column**
      and marks it edited, so a built roof persists its lighting exactly as a dug shaft does. That
      is M6c's placement case, and it fell out of a failing assertion rather than being designed for.
- [ ] **Revisit trigger, real the moment `ApplyFeatures` places anything:** a loaded chunk comes
      back at `Features` so features do not re-run over it. A chunk that A's features wrote into,
      but which was never itself edited, is not stored — so it regenerates from seed and loses what
      A gave it, while A never re-runs. Re-running is not the fix either: a feature re-applied over
      material the player has since mined would undo their edit. Candidates are saving
      feature-affected neighbours, or recording per chunk which features have run. **Neither can be
      chosen against a stage that places nothing.**
- [ ] **`HasEdits` has still never been read by anything**, from M3a to here, despite being
      documented as "exactly the set M6 has to persist". Every gate in the save path is
      `DirtySinceSave`. Kept — one bit, and it states a property the store's bookkeeping does not —
      but recorded as unread rather than assumed load-bearing.

##### The plan's headline worry is measured and it dissolves — backwards

- [x] **A SQLite point query is 6.0 µs, not the 0.1–0.3 ms the crossover table assumed.**
      Read plus deserialise is **35.5 µs**. Over a world where *every* chunk is edited: loading all
      1,521 on one serial lane is **54 ms against ~89 ms to generate the same box on eight
      workers**, and the crossover sits at **2,505 chunks — past the entire fill**. So loading a
      dug-in world is **1.6× faster** than generating it. `--store` in the bench.
- [x] **Batched loads are retired rather than deferred**, per the standing rule — the plan said
      "do not build it before the number says so" and the number says do not. Parallel reads under
      WAL likewise, and that was already the worse option.
- [ ] **Second fallback dissolved in the *wrong direction*** (M5c-2's quad count was the first).
      Worth keeping because it sharpens why the rule pays: estimates are not merely pessimistic,
      they are **guesses about the wrong quantity**. Here the guess was per-load latency; what
      actually decided it was a prepared statement amortising to nothing.
- [ ] **An XML comment cannot contain `--`**, so documenting a `--flag` inside a `.csproj` comment
      is an MSBuild parse error rather than a warning.

- [x] Every race below already has a documented answer in `ChunkPipeline`, written for meshes.
      That is the good news and the trap together: each has to be transferred across on purpose,
      and where a mesh race leaves stale geometry the same race loses a tunnel.
- [x] **An in-memory index of saved chunk keys, read once at world open.** "Does this chunk have
      a blob?" is asked on the sim thread once per chunk entering the load box — a 37×37 shell at
      r16 — so it must be answerable without I/O. ~5 MB at 100,000 edited chunks. **Rejected:
      asking the DB per chunk**, which is 1,521 point queries down one serial lane to fill
      r4h/2v, nearly all answering no. Revisit trigger: ~10⁶ saved chunks.
- [x] **Consultation order: memory → index → seed.** The whole correctness question.
- [x] **The number this plan is most likely to have got wrong is the read side**, and it is named
      for that reason: generation fills r4h/2v in ~89 ms of wall clock (1,521 × 0.47 ms measured
      ÷ 8 workers), but loads run **one at a time** on the serial lane. At 0.1 ms a load the
      crossover is ~890 chunks; at 0.3 ms it is ~300. **So a world dug into heavily can be slower
      to enter than one generated from scratch**, and the crossover is well inside what a real
      base produces. First thing to measure at M6d.
      - [x] **Half-measured at M6a, and in the reassuring direction: deserialisation is 21 µs a
            chunk, not the 0.1–0.3 ms this table assumed.** The remaining half is SQLite's
            per-query overhead, which is M6b's to take. If that lands near 30 µs the crossover
            moves past the whole 1,521-chunk fill and the worry dissolves — but **it is not
            settled either way until the other half exists**, so do not retire the concern on one
            number.
- [x] **Named and not built: batched loads** — one job, one query, many chunks, since SQLite's
      per-statement overhead dominates a 16 KB blob read. The other lever, parallel reads under
      WAL, is the worse one: it contends for the eight workers generation already saturates and
      reopens M2a's decision. Standing rule — measure before building the named fallback.
- [x] **The column restore cannot go where the column is created.** `RetainColumn` runs inside
      `View` inside `ForEachDifference`; a one-chunk step at r16 creates ~37 new columns, ~0.74 ms
      of I/O in one frame, unamortised, against a main-thread budget M2d measured plateauing near
      3% of a core. **Recommended: seed analytically as now, queue a restore, and call
      `QueueColumnLightBuild` when it lands** — M5c-1 built that for exactly "this column's sky
      heights moved". Price: a one-or-two-frame window where a reloaded shaft is lit wrong.
- [x] **Save on unload, from `Forget`'s caller rather than `Forget`.** M2c-2's retention
      machinery is already exactly right — `OutstandingJobs > 0` retains past the last viewer and
      `ForgetIfUnwanted` collects — so **the async save needs no new lifetime concept.**
- [x] **Plus a periodic flush of the dirty set**, because unload-only loses everything for a
      player who digs in one spot for an hour. Tens of chunks at ~1 KB; the argument is purely
      crash safety. **Plus on quit, synchronously.** Built at M6b-3: 30 s, `--autosave=` to sweep
      it, and **measured against real SQLite at 13.4 ms for the first write of five chunks and a
      column, then 3.6–4.3 ms** — a frame's worth, once, every thirty seconds.
- [x] **The mod-set round trip is non-negotiable 2's first real test in five milestones.** Save
      with a mod installed, remove it, reopen — placeholders retaining their identifiers, not
      reinterpreted. Reinstall, reopen — exactly as it was. `Registry` has promised this since M1
      and nothing has ever asked it to deliver.
- [x] **Startup reorders**: open world → read ID table → `Load(source, savedIds)` → `Freeze` →
      construct the streamer. Done at M6b-3, and the real game reports `5 material id(s) restored`
      on reopening a world. `SeedIdTable` throws if anything registered first, which is the right
      failure and a loud one.
- [x] **Break-recipes on the call sites, not only the logic** — M5c-2's two real holes were both
      joins, and replacing `GatherLight(...)` with `null` left 556 tests green. The equivalents:
      never calling the store from `Forget`'s path, and clearing `DirtySinceSave` on completion
      rather than at submission. Both leave a green suite and lose data in play.

##### M6b-3 — The session ✅ **done 2026-08-31, 670 tests passing**

**It was wiring, and the wiring found two defects in Core.** M6b-1 and M6b-2 were exercised by
tests only; this is the part that turns a green suite into play that stops losing work. The
verification is a dig, a quit and a reopen, headless — **9 chunks loaded, 1 column restored, and
414 quads per drawn chunk against a pristine 404**, so the excavation comes back in the geometry
rather than only in a counter. A longer dig moves `drawn` as well, 165 → 166.

- [x] **The startup reorder, and it was the only part with an ordering trap.** `Main.OpenWorld`
      now does: open the store → `ReadWorldHeader()` → null means a new world, else
      `WorldHeaderBlob.Read` → `Content.Load(source, header.MaterialIdTable)` → `Freeze` (inside
      the loader) → build `MaterialTable` → construct `ChunkPipeline` with `store:`.
      **`SeedIdTable` throws if anything registered first**, which is the right failure and a loud
      one. Content loading moved after the command-line pass, because `--world=` and `--nosave`
      decide which table seeds it.
- [x] **`WriteWorldHeader` at open, before any chunk can be written** — that ordering is what
      replaces a world-spanning transaction, because numeric IDs are only allocated during content
      load. Rewritten on **every** open rather than only for a new world, because installing a mod
      extends the table with entries the stored header does not have. For a new world the seed is
      `Main.NewWorldSeed`; for an existing one the header's `Settings` replace it rather than
      sitting beside it, or the unstored terrain comes back a different shape around the saved
      chunks. That is the whole reason the header carries all thirteen `WorldGenSettings` fields.
- [x] **Where the world file lives: `user://worlds/<name>/world.db`.** `user://` was never in
      doubt; the argued part is the extra path segment. A second world — a scratch arm, a new game,
      a backup — is then a directory beside the first rather than a migration of a file at the
      root, and **moving a world file later strands the world in it**. WAL leaves `-wal` and `-shm`
      sidecars next to the database anyway.
      - [x] **A measurement or a screenshot prop does not open the played world.** `--profile`,
            `--fall` and `--debuglamp` leave persistence off unless `--world=` names one, and
            `--nosave` turns it off outright. Two separate reasons: `--mine` would carve up the
            world somebody plays, and **a run that loads what an earlier one saved is not
            comparable with M2d's figures**, because the streaming shape changes when generates
            become serial reads. `--world=` is the explicit opt back in, and it is how M6d
            measures the load path.
      - [x] **An unopenable world refuses to start**, loudly, rather than playing on with
            persistence quietly off — that failure is an hour of digging lost to a line in the log.
            An existing world's `--cavestep` is ignored with a warning, because the header is what
            the stored chunks were generated against.
- [x] **The autosave timer and the quit drain**, and **`SaveAll` was not safe on the sim thread
      after all** — see the two defects below. `_ExitTree` *is* reached: it prints, and the print
      carries the figures from after the write. `--quit-after` uses `SceneTree::quit()`, the same
      teardown a window close takes with `auto_accept_quit` on, which made the windowed close
      verified by proxy — **closed by hand 2026-08-31: closing the window and re-running loaded
      the world back.** No `NotificationWMCloseRequest` handler is needed.
- [x] **Headless verification that the streaming figures are unchanged.** The default run should
      still report **1521 viewed / 165 drawn / 1926 jobs** — M2d's numbers, which M5c-1 also had to
      preserve. A world with nothing saved takes the generate path throughout, so any movement in
      those figures is a regression rather than persistence doing its job. **Confirmed to the
      digit.**
- [x] **Played 2026-08-31, and the verdict is yes.** *"I closed it and re-ran it and it loaded the
      last world I was in."* That is the milestone's sentence confirmed by a person rather than by
      a counter, and it closes the one thing M6b-3 could not do for itself — the windowed close,
      which every headless run could only reach by proxy.

###### The two defects the wiring found, both in Core, both on the synchronous save path

- [x] **`IWorldStore`'s "one caller at a time" contract had a second caller.** M6b-1 justified the
      contract by *every call arriving on the serial lane*; M6b-2 added `SaveAll` on the sim thread
      and justified it by *quitting being when the scheduler stops*. Both are reasonable and
      together they are false the moment `SaveAll` runs anywhere else, which is what an autosave
      timer is. **The obvious hazard is a save in flight; the real one is a load** — three job
      types touch the store and `LoadChunkJob` is submitted once per stored chunk entering the box,
      so walking back into a region you dug means the lane is usually inside the store. Against
      SQLite an overlap is two threads binding parameters on one shared prepared statement.
      `ChunkPipeline` now counts store jobs in flight and `SaveAll` drains the lane first.
      - [x] **The counter comes down on every outcome, not only on success**, or a dropped load
            wedges the drain for its whole timeout at every quit.
      - [x] **The drain terminates because nothing it does submits** — jobs only reach the
            scheduler from `Pump`, `SubmitSaves` and `OnColumnCreated`, none of which runs there.
      - [x] **`_ExitTree`'s order — pipeline, then scheduler — is now load-bearing twice.**
            Disposing the scheduler first would drop whatever was queued on the lane, and a dropped
            save is a deleted tunnel.
      - [ ] **No test for the five-second drain timeout**, deliberately: it costs five seconds of
            suite time to prove a branch that only runs when something else is already broken.
- [x] **The synchronous save had no fault path.** `ApplySave` has restored a failed batch since
      M6b-2 — pending lists, column dirty bits, index claims — and `SaveAll` had none of it, while
      clearing the flags *before* the write. A store that threw left the world marked saved and
      never written. **The answer existed, in the twin of the method that needed it**, which is
      M6b-2's "every race already has a documented answer" one layer further down.
- [ ] **The Godot export with SQLitePCLRaw's natives is still unverified.** Running works: the
      native `e_sqlite3` resolves through the generated `.deps.json` under `EnableDynamicLoading`
      with no `RuntimeIdentifier`. M6b-1 recorded the export as the part to check and it is still
      the part to check.

#### M6c — Placement and volumetric inventory ← **split three ways 2026-08-31, before any code**

**Split up front on M6b's grounds**, which is what M2c and M6b both record as the reason they went
smoothly. M6c-1 is the brushes — pure Core, no inventory, no Godot, no threads. M6c-2 is the
inventory and its persistence, which is the world-header format bump `WorldHeaderBlob.Version`
predicted in its own remarks. M6c-3 is the binding and the play.

##### What the planning pass settled before any code

- [x] **Only air is replaceable, and the argument is the hardness gate rather than taste.** A
      brush that overwrites existing material is a way to delete anything without mining it: place
      dirt into a bedrock voxel, then mine the dirt with a `hand_pick`, and bedrock is gone. That
      makes `Carve.Take`'s gate — the mechanic M3c exists for — bypassable by the *other* verb.
      So a voxel that is not air lands in an `Occupied` bucket and is not written.
      `IVoxelEditor`'s own comment has said since M3a that placement "will need to know it is
      replacing something **replaceable**"; at M6 replaceable means air, and **M8 is where the
      word earns its generality**, because water should be displaceable and rock should not. Same
      revisit trigger `VoxelRay` and `Carve` already carry.
- [x] **A consequence, and it corrects this plan: `SetVoxel`'s "material is already there"
      early-out still has no caller that fires it.** M3-PLAN justified it by the carve rewriting
      air as air; M3c made `Carve.Take` skip air first, and the comment was updated to predict
      that *"M6's placement is exactly the shape of caller that rewrites what is already there"*.
      Refusing non-air means placement decides the bucket from a read it has to do anyway and
      never offers the write. **Keep the early-out and say plainly that it is unreachable by
      design** — M4a's zeroable-contact-epsilon category, third outing — rather than deleting it
      or contriving a caller for it.
- [x] **The brush lattice is its own constant and deliberately *not* `LightCellPos`.** Both are
      1 m and share an origin, so reusing the light cell would work today and be free. It is a
      trap: **the 1 m light cell has a live revisit trigger** (M5d: a `hand_pick` tunnel reading
      blobby), and shrinking it to 0.5 m would silently halve the brush lattice — changing where
      every brush lands, in a milestone that has nothing to do with lighting. Two things that are
      the same size for unrelated reasons want two constants.
- [x] **The anchor is the 1 m cell containing `VoxelHit.Previous`**, the last empty voxel before
      the surface — the point M3b built and recorded as *"where M6's placement goes"*. Snapping is
      an arithmetic right shift, not a division, for the reason `VoxelPos` gives about negative
      coordinates.
- [x] **`PlaceResult` takes `CarveResult`'s shape**: `Candidates` = `Placed` + `Occupied` +
      `InsidePlayer` + `Unreachable`, with `IsAccounted` asserted by every test, for the reason
      that struct records — *"a `continue` added later that is counted nowhere"*. M6c-2 adds a
      fifth bucket for a short inventory; it is deliberately **not** added now, because a counter
      built before its consumer is a number waiting to be believed.
- [x] **The player's box is a parameter, not a lookup.** Core has no player, and the brush needs
      only an `Aabb`; `Aabb.VoxelBounds` already answers "which voxels does this body overlap"
      under the half-open rule. Null means no body, which is what the tests and any future
      non-player placer use.
- [x] **A ramp's orientation is a parameter of the brush, computed by the caller.** M6c-1 takes a
      facing; M6c-3 derives it from the face normal and the player's yaw. That keeps the first
      non-scalar brush parameter out of Core's business while leaving *where a brush lands* — the
      lattice — in Core, which is M3d's rule about a game rule a multiplayer server would need.

##### M6c-1 — The brushes ✅ **done 2026-08-31, 689 tests passing**

- [x] **`Place.*` calls `IVoxelEditor.SetVoxel` per voxel, exactly as `Carve` does.** A 1 m cube
      is 64 voxels and writing them straight into `chunk.Voxels` is faster, obvious, and skips
      **four** things silently: the sky-column update, the light rebuild queue, the eight-chunk
      border rule, and `HasEdits` — which at M6 means the placement is not saved.
      **Correction to the handover: M5c-1's trigger is about `ApplyFeatures`, which still places
      nothing, so M6 does not fire it.** The risk is the same one; the trigger is not.
- [x] **One shared `Put` gate for every brush**, the counterpart to `Carve.Take` and load-bearing
      for the same reason: M3c measured that removing the hardness check from `Take` failed nine
      tests across both shapes, which a forked copy would not have done.
- [x] **The brushes are staircases, not slopes.** M4d's deliberately-not list rules out non-cube
      collision shapes as *"a voxel is one material or another" violations wearing different
      hats*, so a ramp brush is an **arrangement of 0.25 m cubes inside a 1 m cell** — a
      four-step staircase. M3c's *"sub-voxel means the voxel is small, not partly full"* restated
      for placement, and it lands well: 0.25 m is comfortably inside M4b's 0.5 m step height, so
      a placed ramp is walkable by physics that already exists. A slab is 4×2×4, 0.5 m thick.
- [x] Brushes are **methods, not an `IShape`**, following `Carve`'s recorded argument: no second
      implementer, and a ramp wants an orientation where a sphere wants a radius. **A ramp's
      orientation is the first brush parameter that is not a scalar** and the first thing in this
      game with a rotation at all — from the raycast face normal plus the player's facing.
- [x] **Brush anchoring: snap to the world 1 m lattice** (recommended, Matthew's to overrule).
      Brushes tile with no seams and no one-voxel gaps, and **the slab and ramp only make sense on
      a lattice at all** — a ramp that is not grid-aligned cannot be tiled into stairs. So the
      lattice is what makes the brush *set* coherent, not just the cube; DESIGN's *"fine detail
      comes from carving, not from placing"* is the argument. **Goes in Core** on M3d's grounds,
      as the counterpart to `Carve.SwingCentre`. **Revisit trigger: M6d plays it and
      lattice-snapping reads as unresponsive.**
- [x] **Material inside the player: refuse the overlapping voxels and place the rest**
      (recommended). Least surprising; pillar-jumping still works because mid-jump the cell below
      is not overlapping. **It does not retire `TryUnstick`** — M9's collapse puts material inside
      the player by accident and has no alternative — it stops placement being a second,
      deliberate caller of a rule whose only honest home is "something went wrong".
- [x] **A placed ramp is walked up by the physics that already exists — tested, not argued.**
      Two ramps and a landing, 180 steps of walking east, no jump ever. It failed the first time
      and the failure was the test: the body climbed both flights, walked off the end of the
      landing and fell back to the floor, reporting feet at 1.0 m — which reads exactly like
      "it never climbed".
- [x] **`VoxelSphere` extracted from `Carve` so both verbs share one membership test.** A sphere
      carved and a sphere placed at the same radius have to be the same voxels, or filling in a
      hole leaves a rind. **And the recipe that means anything against a shared definition is the
      one that *un*-shares it** — mutating `VoxelSphere` itself moves both sides together and the
      congruence test still passes, which is M6a's writer/reader-pair lesson in a third shape.
- [x] **`IsAccounted` was one line from being true by construction.** The first tally computed
      `Candidates` as the sum of its own buckets, so the invariant could never fail and every test
      asserting it would have passed forever. `Tally.Candidate()` is now a separate call from
      `Tally.Add(bucket)`: the shape counts, the gate decides, and a `continue` between them fails
      eight tests.
- [x] **Nine break-recipes, eight caught outright.** The hole was a fixture: the sphere congruence
      test used an arbitrary centre and radius, under which no voxel centre lands at exactly the
      radius, so `<` and `<=` agree everywhere and a divergent private copy passed. Re-fixtured
      onto a voxel centre with a whole-voxel radius, and **the fixture now asserts it reached the
      boundary case** rather than leaving that to luck.
- [x] **`TestWorld` gained the write half** so a placement and a walk happen in one world rather
      than in two doubles that agree with each other.
      - [ ] **Tidy: merge `CarveTests.FakeEditor` into `TestWorld`.** Same idea, uniform background
            instead of a predicate. Two doubles for one interface is the drift the project has
            already been bitten by twice; not urgent, and not M6c's job.

##### M6c-2 — The inventory ✅ **done 2026-08-31, 719 tests passing**

- [x] **Volumetric inventory in litres.** A 0.25 m voxel is 15.625 L; a 1 m brush cube is 64
      voxels = **1,000 L = 1 m³**, the round number that makes the unit worth having.
      `CarveResult.Removed` is already documented as the number to convert.
- [x] **Unbounded at M6** (recommended). Bounding it with no containers, conveyors or crafting
      means the player fills up and stops — a constraint with nothing on the other side. **Honest
      cost: M6 then cannot answer whether volume is a real constraint**, so DESIGN's *"makes big
      excavations meaningfully productive"* stays argued rather than played. **Revisit trigger:
      M10 containers or M11 crafting, whichever gives capacity somewhere to go.**
- [x] Placement consumes it; a short inventory places what it can, in another bucket.
- [x] **The inventory persists in the world file keyed by numeric material ID**, safe for exactly
      the reason the blobs are — same database, same table. Anywhere else and it has the identical
      mod-load-order failure with no fix. If the held tool is saved, `GameContent.Tools` has
      already written down the cost: *"it will be the same three lines."*
- [x] **This is the `WorldHeaderBlob.Version` bump its own remarks predicted**: *"Expect this to
      move during M6. M6c adds the player's position and volumetric inventory, and that is a layout
      change."* Two things follow. The version byte must actually move, and **an old world must be
      refused rather than read** — which is the trade the format is making, and the only person
      holding a saved world mid-milestone is Matthew.
- [x] **Settled 2026-08-31: the player's position goes in, with the walk/fly mode, and not the
      held tier or the facing.** Matthew's call on the recommendation. Both omissions are recorded
      on `PlayerSave` with their cost, which is one more format version — mid-milestone, that is
      the trade this format already makes. **Revisit trigger: M6d plays it and reopening
      face-first into a wall reads as wrong.**
- [x] **Stored as voxel counts and presented in litres.** A voxel is the only granularity anything
      can enter or leave the world in, so counting is exact and every litre figure is a
      presentation of an integer; litres in a float would drift across a session and "places what
      it can" would stop having a clean answer. **Revisit trigger: the first thing that moves a
      quantity which is not a whole number of voxels** — M8's volume-conserving fluids most
      likely — at which point this wants fixed-point litres.
- [x] **The credit is per material inside `Carve.Take`, after the write.** Not from
      `CarveResult.Removed`, which is one number for a bite that can take two materials. **The
      debit is inside `Place.Put`, after the write, with the stock consulted last** — charging
      before the voxel is known to be fillable would make a wall built against a cliff cost the
      volume of the cliff.
- [x] **Version 1 is migrated rather than refused, and the policy is now stated: an additive
      version migrates, a version that changes what a field means is refused.** A v1 header is a v2
      header without its two trailing sections, so reading one supplies two defaults that are true
      statements about it. **Verified against Matthew's own v1 world**, which opens, migrates and
      loads its eight saved chunks.
- [x] **Seventeen break-recipes, and four needed the test fixed rather than the code.** Three were
      the same new variety — **a corruption test that throws from a different check than the one
      it is named after**, because the header validates in layers and `Assert.Throws` cannot tell
      which guard fired. The fourth was the familiar one, a fixture under which both arms agree.
      The count check's "at or past the table size" arm is now recorded as belt and braces with no
      test, because it is unreachable without also tripping a row check.

##### M6c-3 — The binding, and the play ✅ **built 2026-08-31, 724 tests passing — the play is Matthew’s**

- [x] Right mouse places, **B** cycles the brush, **V** cycles the material. `VoxelHighlight` gained
      a size and a colour, and there are now **two**: the 0.25 m dark box around the voxel a swing
      would take, and a 1 m yellow box around the metre a brush would fill. In a screenshot they
      nest and read as *this voxel / this metre*. **Confirmed by screenshot, which is the only
      instrument that can** — headless has no renderer, so a highlight that scaled wrongly or never
      showed would look identical in the logs.
      - [x] **The body is passed while walking and not while flying.** In fly mode the body is inert
            and sits wherever the last F left it, so passing it would refuse voxels around a
            position the player is no longer at.
      - [x] **The material selects itself when nothing is selected**, taking the largest holding, so
            mining a bite and then reaching for the other button needs no key press in between.
            −1 means "carrying nothing" rather than air, because air is a legal ID that `Place`
            refuses and the sentinel would turn an empty inventory into an exception.
      - [x] **`Place.FacingFrom` moved into Core** beside `TargetCell`, on M3d's grounds: which way
            a ramp rises is a game rule a server would have to agree about. It rises **away** from
            whoever placed it, so walking forward onto it climbs it.
- [x] `PlaceController` is Godot-free for the reason `MiningController` is: `--place` has to drive
      a headless profile, because a debug bind nobody has run headlessly is a bind that does not
      work. `--stock=` seeds the inventory so the run measures the brush rather than the carve,
      and `--brush=` picks the shape.
      - [x] **Measured: `place ms` mean 0.038, max 0.186** against a 16.7 ms frame, and against
            mining's 0.086 mean for a sphere half the size. **Do not build anything to make
            placement cheaper.** Streaming untouched: 1521 / 165 / 1926 at 404 quads.
      - [x] **The round trip works end to end**: build, quit, reopen, and the inventory comes back
            to the litre, the structure loads, and the player is put back where they were. Run 2
            spawned at **4.00 m instead of 1.25 m** because run 1 had built a cube on the spawn
            column — nothing was asserting that, and it is the most convincing evidence in the set.
      - [x] **A profile run saves no player**, because the body never spawns in fly mode. Right: a
            measurement should not move where you live.
- [x] **Two defects the wiring found.** `ChunkPipeline.WriteWorldHeader` was a second sim-thread
      caller of the store with no drain — correct while the only call was at open, before a single
      job existed, and wrong the moment M6c-3 put it on the autosave timer. **Third time this
      milestone that a safety argument turned out to be about the caller rather than the code.** And
      the cell outline leaked its RID at exit, which Godot reports without saying which one.
- [x] **Played 2026-08-31, and the verdict is that the brush set is the wrong granularity for
      stairs.** *"Nah doesn't auto climb but later when we place blocks one at a time this will
      work better."* Built with cubes, and **a cube riser is 1 m, exactly twice M4b's 0.5 m step
      height** — so cube stairs can never be walkable, by arithmetic rather than by defect. The
      ramp brush is the walkable one and its Core test still holds; what the play showed is that
      **one ramp buys exactly one metre of rise**, and a staircase taller than that means placing
      each successive ramp a cell higher, aimed at the top face of the last. Fiddly enough that
      nobody will do it.
      - [x] **The lattice snap itself drew no complaint**, which is the question this play was
            staged to answer. Recorded as silence rather than approval — the objection was
            about brush *size*, so the snap was never really under test. **The M5d-shaped
            distinction: probed and passed, versus never provoked.** The revisit trigger stays live
            for M6d.
- [x] **Fixed 2026-08-31: a saved flying player came back at the origin, underground.**
      `SavedPlayer()` asked the live camera for the eye, which is true during a frame and not
      during teardown — and `_ExitTree` is when it was asked. **The rule: state that has to
      survive a quit must be captured while the frame is alive.** The eye is cached every
      `_Process` while flying and the save reads the cache. The walking half never had the problem
      because it reads plain C# state.
      - [x] **A second bug beside it**: the open-time header write happens before the body exists,
            so `SavedPlayer()` returned null and **cleared the saved position on every launch**,
            leaving it cleared until the first autosave. It now falls back to what the world was
            opened with. Pinned by a run that quits at frame 2.
      - [x] **The quit prints what it saved**, because a wrong position is invisible until the next
            launch puts you somewhere strange.
- [x] **Single-voxel placement, and it is the gap the play found.** ✅ **built at M6d-1** as
      `Place.Voxel` on the Alt modifier. Mining has two shapes —
      `Carve.Swing`'s sphere and `Carve.Chip`'s one voxel on a held modifier — and M3c argued the
      precision one exists *"for the person who has stopped excavating and started building"*.
      Building itself has only the bulk tool. `Place.Voxel` is the mirror: the gate, the buckets
      and the inventory debit already exist in `Put`, so it is a shape rather than a mechanism.
      - [ ] **The key is the open question, and Ctrl is already spoken for.** Ctrl+left is the
            mining chip, and TODO already records that Ctrl doubles as FlyCam's descend, with Alt
            named as the better key. Deciding both at once is worth one pass rather than two.
      - [ ] **It does not retire the brushes.** M3c's argument transfers exactly: a single voxel is
            1/64th of a cube, so a wall built voxel by voxel is 64x the clicks, and DESIGN's
            *"fine detail comes from carving, not from placing"* becomes false in the other
            direction if precision becomes the default. Precision is the exception and the
            exception is worth a held key.

#### M6d — Selection, feel and measurement ← **split three ways 2026-08-31, before any code**

**Split on M6b's and M6c's grounds, and the order is not the plan's.** The plan says
measure first; the play-test of M6c changed that, because it found the brush set missing
its precision shape and **M6d's own play item asks "whether four shapes are the right
four"**. Judging feel on a verb that is knowingly incomplete spends the play
re-discovering something already written down. So: build the fifth shape, then measure
everything once, then play once.

##### M6d-1 — Precision placement, and the modifier keys ✅ **done 2026-08-31, 727 tests passing**

- [x] **`Place.Voxel`, the mirror of `Carve.Voxel`.** `Put` already holds the gate, the
      buckets and the inventory debit, so this is a shape and a call site rather than a
      mechanism. It takes `VoxelHit.Previous` — the empty voxel before the surface — for
      the reason `Carve.Chip` takes `hit.Voxel`: the outline then *is* the thing about to
      change, rather than the centre of something extending past it.
- [x] **The precision modifier moves from Ctrl to Alt, for both verbs.** Three things fall
      out at once and they have been recorded separately since M3d:
      - [x] **It closes the known Ctrl clash.** Ctrl is also FlyCam's descend, so precision
            mining while flying sinks the builder ~1.8 m between chips. TODO has named Alt
            as the better key since M3d and left it *"pending a play-test"*; M6c's play is
            that play-test, arriving from the other verb.
      - [x] **Alt is *crawl* in fly mode**, so "slow down to do fine work" pairs rather
            than fights — the argument that made it the better key in the first place.
      - [x] **One modifier meaning "precision" across mining and building** is worth more
            than either fix alone: Alt+left takes one voxel, Alt+right places one.
- [x] **The highlight has to follow the modifier**, or the player cannot see what precision
      would do. Holding it shows a voxel-sized box at the voxel that would be filled,
      instead of the metre-sized cell. `VoxelHighlight` takes its size per show rather than
      per instance, so this is one outline changing scale rather than a third RID.
- [x] **It does not retire the brushes, and M3c's argument transfers exactly.** A voxel is
      1/64th of a cube, so a wall built voxel by voxel is 64× the clicks, and DESIGN's
      *"fine detail comes from carving, not from placing"* becomes false in the other
      direction if precision becomes the default. Precision is the exception and the
      exception is worth a held key.
- [x] **Measured, and it is the opposite way round: precision costs about twice as much per click
      while doing a sixty-fourth of the writes.** `--place` is 0.040 ms mean and places 23 voxels a
      click; `--preciseplace` is 0.077 ms mean and places 1. So precision is inefficient in a
      *second* way beyond the click count, which strengthens the argument for keeping the brushes
      as the default. **The cause is inferred and not measured** — presumably the raycast and the
      edit's clone-and-relight marking dominating, so one voxel in fresh air pays a whole chunk
      clone while sixty-four in a cell amortise it. **M6d-2's to settle.**
      - [x] **Settled 2026-09-01, and the conclusion above is corrected: precision does not cost
            more per click.** The 0.040-against-0.077 pair is a comparison of *means*, and over
            seven reps precision's **median** click is 0.004-0.007 ms against the brush's
            0.013-0.030 — two to four times cheaper, as the voxel count predicts. The mean is
            dragged by a tail: precision's worst click is 200-300x its own median. **The
            hypothesis's mechanism was right** — 0.19 clones and 0.06 relights per click while
            placing 0.40 voxels, against the brush's 0.00 and 0.00 for 7.73 — **so the brushes
            stay the default on amortised-clone grounds, not per-click ones.**
- [x] **Amended 2026-08-31 after a conversation about Minecraft accuracy: Ctrl and Shift had been
      swapped since M2.** Minecraft is Sneak = Shift, Sprint = Ctrl, and in flight Shift descends
      while Ctrl hurries; this project had the two the other way round. Muscle memory for these
      keys is decades old against this project's one day, so accuracy wins. **It also removes the
      cause of the M3d note rather than dodging it** — Ctrl-as-descend is what made precision
      mining sink the builder — and Alt stays precision, because Ctrl-as-sprint would have been a
      worse pairing for fine work than crawl.
      - [x] **Sneak is a physics rule, not a keybinding.** While grounded and not rising, a
            horizontal move that would leave the body unsupported is refused, **per axis** so that
            walking diagonally at a drop slides along the lip rather than stopping dead. In a game
            about standing on ledges over shafts you dug yourself, that is the difference between
            building at the edge and falling down your own mine.
      - [x] **"Supported" is step-down's own probe, called the same way**, and the shared
            definition is the load-bearing part: a stricter test would refuse moves step-down would
            have caught, stopping the player at the top of a staircase they could safely walk down.
      - [x] **Sneak beats sprint when both are held**, and not by convention here: sprinting off a ledge
            you were carefully sneaking along is the accident the rule exists to prevent.
      - [x] **Five break-recipes, two of which needed the test fixed.** Both were the scenario
            ending before it reached the case: the mid-air guard cannot be caught by "does not stop
            a fall", because the rule only trims horizontal movement — what it protects is *air
            control*, and an unguarded rule eats a jump. And the per-axis slide test finished its
            travel at the moment it reached the lip.
      - [x] **Two fixtures were wrong about the physics rather than the reverse**, and both are
            behaviours somebody would otherwise file as bugs: a sneaking body **hangs most of
            itself over the drop** (support is "some voxel under the footprint"), and **a gap
            exactly the body's width is walked across while sneaking**, no jump needed.
- [x] **`--preciseplace` exists to test the wire, not to be a feature.** Every layer of precision
      placement has a Core test and the join between the held key and `Place.Voxel` had none,
      which is M5c-2's shape of hole exactly. The report names the shape actually used rather than
      the brush selected, because a precise run labelled "cube brush" is M3d's
      chips-sharing-the-swing-line trap repeated.

##### M6d-2 — Measurement ✅ **done 2026-09-01, 734 tests passing**

**Everything it measured already existed and ran.** No new mechanism was needed for any line —
only instruments, and the instruments are the thing that found the surprise.

- [x] **The headline is answered: entering a dug world is not slower than generating it, it is
      indistinguishable.** Standing start at r4h/2v, two reps each: generate `first draw`
      3.46 / 3.43 s and `all settled` 3.81 / 3.80; load with 19 stored chunks in the box
      3.48 / 3.43 and 3.85 / 3.80. Moving at 6 m/s with **71 chunks loaded and 44 columns
      restored**: 3.84 / 3.82 against 3.82 / 3.80, settling 4.62 / 4.61 against 4.64 / 4.62.
      **The load-versus-generate gap is smaller than the rep-to-rep gap.**
      - [x] **The limitation is stated rather than buried.** 176 stored chunks is the most this
            world could be dug to, against 1,521 tracked, so the predicted 2,505-chunk crossover
            is confirmed **in direction and not in fact**. The scripted flythrough is a straight
            line along +X, so passes deepen one trench rather than covering ground: 99 → 104 →
            112 → 123 → 176, plainly asymptotic. **The concern moves from "predicted to bite at
            2,505" to "measured not to bite at 176"; it is not retired.** **Retired 2026-09-03 for the uniform shape, by the played world: 4,584 stored, 953 loaded at a standing start, light settled 5.97 s against 5.88 s generating, and `held 0` through 1,528 loads under a 40 m/s fall. The trigger is now thousands of *non-uniform* stored chunks.**
- [x] **The finding nobody was looking for: the box-difference sweep costs two to four times the
      whole pipeline update, and no profile since M2d has measured it.** `MoveViewer` is called
      from `_Process` *before* the bracket that produces `update ms`. Timed as `viewer ms`:

      ```
                                        tracked   viewer ms mean / max   update ms mean
      r4h/2v   6 m/s  no world open        1521    7.09-9.02 / 15.3           2.34
      r4h/2v   6 m/s  world open           1521    9.52-10.0 / 15.6           2.33
      r6h/3v   6 m/s  no world open        3179   13.21      / 19.61          4.05
      r4h/2v  40 m/s  world open + mining  1521    4.84      / 13.69          5.31
      ```

      - [x] **`main %` understates the main thread and always has** — it is computed from
            `PipelineUpdate` alone.
      - [x] **Sub-linear in radius**: 2.09x the tracked chunks for 1.47x the sweep, so it is not
            purely per-chunk work.
      - [ ] **What the sweep is actually spending is deliberately not chased.** M6d-2 is
            measurement. Retaining and releasing light grids and sky columns is the obvious
            suspect. **Wants its own pass, and the number to beat is 9 ms at r4h/2v.**
- [x] **`Forget` becoming a write is a non-event, and it is the design answer that made it one.**
      Measured on both threads, printed side by side and never summed: a 60 s dig at 6 m/s costs
      **sim 2.4-3.6 ms in total** (0.07-0.11 ms a batch) against **worker 22.6-25.9 ms**; 15 s at
      40 m/s costs **sim 2.829 ms** (0.0345 a batch) against **worker 37.4 ms**.
      - [x] **The burst the plan feared never forms, and `SubmitSaves` running every frame with
            one batch in flight is why.** Largest batch across every run measured is **6**, not a
            37x37 shell. That comment was written as a transaction argument and turns out to be
            the burst bound too.
      - [x] **The control is the load-bearing half: `viewer ms` with no world open at all
            overlaps the figure with one open**, so the sweep's cost predates M6 and belongs to
            something else. If `SaveQueueMillis` and `SaveWriteMillis` ever converge, the write
            has come back onto the sim thread and no other line in the report would change.
- [x] **Why precision placement costs more per click than a brush: it does not, and M6d-1's
      conclusion was read off the mean.** Seven reps each, standing start, r4h/2v:

      ```
                mean          p50           p95           max         clones remesh relight placed
      brush   0.016-0.028   0.013-0.030   0.029-0.045   0.056-0.129    0.00   0.05   0.00    7.73
      precise 0.027-0.058   0.004-0.007   0.059-0.103   0.883-1.790    0.19   0.20   0.06    0.40
      ```

      - [x] **Precision's median click is two to four times cheaper**, exactly as one voxel
            against sixty-four predicts. The mean is twice the brush's because precision's worst
            click is **200-300x its own median** against the brush's 2-5x, and one or two
            outliers move an 88-sample mean by the whole reported difference — two reps disagreed
            at 0.058 and 0.031 before five more were run.
      - [x] **`SampleSeries`' own doc comment predicted this**: *"a mean hides exactly the thing
            worth measuring."* Fifth outing, and the first time it caught a conclusion already
            written into this file.
      - [x] **The hypothesis's mechanism is exactly right and is what the tail is made of.**
            Precision pays **0.19 clones, 0.20 edit remeshes and 0.06 relights per click while
            placing 0.40 voxels** — about half a chunk clone per voxel. The brush places 7.73
            voxels for **0.00 clones and no relights**, because they land in one already-cloned
            chunk and its 56 refused voxels never reach `SetVoxel`. **M3c's argument for brushes
            as the default survives, on amortised-clone grounds rather than per-click ones.**
- [x] **`Compact()` stays off, re-measured rather than re-asserted.** On a carved chunk it costs
      253.2 µs, **1,276% of the whole write, and saves 0.0%**; on a stripped chunk 100.8 µs, 888%,
      for 7.5%. **Compression is 22-31x**, clear of the recorded ~3x revisit trigger.
- [x] **Save size after a real session: the plan was pessimistic by about 2x, in the safe
      direction.** Predicted ~1 KB a chunk and a few hundred KB a session; measured **480-515 B
      per chunk blob**, the played world at **36 KiB** and a four-pass dug world at **96 KiB for
      176 chunks and 80 columns**.
      - [x] **189 B and ~500 B are two different quantities and neither is the other.** The
            bench's 189 is the marginal cost of variedness; the game's ~500 is a whole stored
            chunk with its sky column. Quoting either as the other is this project's recurring
            denominator error.
- [x] **Used `update ms`, not `frame ms`** — and `viewer ms` exists because `update ms` turned out
      not to cover the sweep. The headless frame-time trap has now appeared four times across M2d,
      M3d and M4d; **this is the first time the directly-timed number was itself incomplete**,
      which is a different failure and worth keeping apart from it.
- [x] **A reporting defect, found by being misled by it.** `PlacementLines` printed per-click
      voxel counts at `F0`, so an arm placing 0.40 voxels a click rendered as `0 placed` — reading
      as *the arm did nothing* for an arm that was working. Now `F2`. **First time this project's
      print-the-denominator habit has produced a wrong reading through its absence rather than an
      unreadable one.**
- [x] **And a fixture trap, caught by the numbers refusing to make sense.** The placement arms run
      at 6 m/s placed *nothing at all*: `--place` aims along the look direction, deliberately, and
      the flythrough holds the camera 4 m above the surface looking level at sky. **A moving
      placement profile is not a thing this instrument can produce.** Zero placements is a loud
      failure; the quiet version was the standing-start run before it, where both arms spent most
      of their clicks refusing into a cell they had already filled.
- [x] **Instruments built, because the measurement needed them.** `SaveBatches`,
      `LargestSaveBatch`, `SaveQueueMillis` and `SaveWriteMillis` on `ChunkPipeline`;
      `SaveWorldJob.ElapsedMillis`; a `ViewerMove` series on the profiler; `PersistenceLines()` in
      the report. **`SaveQueueMillis` is timed after `QueueSave`'s early-out on purpose** — most
      of an unloading shell is untouched terrain leaving through that line, and folding it in
      would price the burst at the cost of not saving.
- [x] **The default headless run is unchanged to the digit** — 1521 viewed / 165 drawn / 1926 jobs
      at 404 quads. An instrumentation pass has to prove that before any of its numbers are worth
      reading.

##### M6d-3 — The play, and the first real UI ← **the UI is built; the play is Matthew's**

**Built 2026-09-01, 739 tests passing.** The three play items below are the point of this part
and no amount of code closes them — a sandbox cannot be judged by the person who built it out of
the report it prints.

- [x] **Play: dig a tunnel, fly away until it unloads, come back — it works.** Played
      2026-09-01. **This is the milestone's whole reason for existing**, the thing M5d-2's play
      kept running into, and a yes/no no test produces. It is now yes.
- [x] **Play: quit and reopen — it works.** Played 2026-09-01. Same world, same inventory, same
      place. Confirmed by proxy at M6b-3 with a headless dig-quit-reopen; **this is the first time
      a person has closed the window and found their own work still there.**
- [x] **Play: build a room — done 2026-09-01, and it reads as a room.** Walls, floor, ceiling
      and a broad flight of stairs down into it. Not "place a block". That
      is what says whether the 1 m lattice is right, whether four shapes are the right four, and
      whether carving-for-detail and placing-for-bulk actually divides the way DESIGN claims.
      - [x] **Part-answered 2026-09-01: the ramp brush is the right tool for stairs, and it is the
            fast path.** In Matthew's words, *"definitely using the ramp mode, honestly using the
            dig tool to get those kind of stairs would take me a lot longer"*. **That validates
            M6d-1 going first in the split** — M6c found cube stairs unwalkable, M6d-1 added the
            shape that fixes it, and the play says the fix is what people reach for. **DESIGN's
            carve-for-detail / place-for-bulk split stands unamended, with stairs on the *place*
            side**, which is where it always put them.
      - [x] **Caveat for anyone reading this project's screenshots: Alt+PrintScreen holds Alt, so
            the status line reads `ONE VOXEL (Alt)` whatever the player was doing.** The HUD is
            answering the keyboard truthfully and is **deliberately left alone** at Matthew's
            instruction. It was believed once and a wrong conclusion was built on it — *"you're
            cutting the stairs, not placing them"*, with a proposed DESIGN amendment that turned
            out not to be needed. **The first trap here where the instrument was a person**, and
            the tell was available: the status line is live input state, so it says what is true
            at the instant of capture rather than during the work. Every other element in the
            frame is honest, because none of the others are keyed off a key being down.
      - [x] **The walls-and-floor half is answered too: a room was built and it reads as a room.**
            Played 2026-09-01 — walls, floor, ceiling and a broad flight of stairs down into it,
            about 3,360 voxels placed (stone 2,194,047 to 2,141,516 L across the two shots).
            **So M6 own play is complete**, and the 1 m lattice, the four shapes and the
            carve/place divide are all judged rather than argued.
      - [x] **And the ID-order slot cost fired in play, exactly as written down.** Acquiring 16 L
            of lamp — one voxel — inserted a slot at 3 and pushed stone from **3 to 4** between
            two screenshots. That is `MaterialInSlot`'s recorded revisit trigger, *"the play
            reports a slot moving under their fingers"*, observed rather than predicted.
- [x] **Hotbar / material selection UI — built.** `Overburden.Game/Ui/Hud.cs`: a nine-slot row
      with a colour swatch, name and litres per slot, a crosshair, and a status line naming the
      brush, the tier and the keys.
      - [x] **The number row moved to materials and the tiers moved to T**, which is the decision
            this item names and does not settle. Both want 1-9 and only one is what a hand expects
            to find there: Minecraft's number row is the hotbar and holds what you build with.
            **The same muscle-memory argument that swapped Ctrl and Shift on 2026-08-31**, and
            Matthew's call again. Two picks ship, so the tiers lost one press for one press.
            **If a mod ever ships eight picks that trade inverts** — the tiers want the row back
            and the materials want somewhere else to go.
      - [x] **`SelectTier` stopped being polled every frame and became an event on the key.** Not
            tidying: the poll needed a `ReferenceEquals` guard purely to avoid re-announcing sixty
            times a second, which is a guard that existed because the input was read in the wrong
            place.
      - [x] **`VolumetricInventory.MaterialInSlot` is the row: carried materials in ascending ID
            order.** The hotbar is what you carry, not what exists — same argument `NextMaterial`
            already made.
            - [x] **The cost is stated rather than hidden: acquiring a material you did not have
                  shifts every slot above it.** Mine your first dirt and the stone on 1 may become
                  2 under your hand. Accepted because the fix is a slot assignment that has to
                  survive a quit, which is a header field and a format version, in the part of M6
                  most likely to be redone. **Revisit trigger: the play reports a slot moving
                  under their fingers.**
            - [x] **ID order beat first-acquired order on the one case that separates them.**
                  Acquisition order is stable within a session and loses across a quit, because
                  `Reset` replays the header's entries and acquisition order is written down
                  nowhere. **A rule that is always the same beats one that is usually better and
                  occasionally lies.** `TheRowIsUnchangedAcrossASaveAndReload` is the test the
                  rejected alternative fails.
            - [x] **It gave `Entries()`' sort a second and louder dependant.** That sort exists so
                  the same inventory writes the same bytes; it is now what a number key *means*,
                  and a shuffled hotbar moves materials mid-build where a shuffled save is merely
                  unanswerable.
            - [x] **Four break-recipes, four caught**: drop the sort (2 tests), drop the
                  negative-slot guard (1), off-by-one the upper bound (4), ignore the slot
                  argument (3).
      - [x] **One definition of what stone looks like.** `VoxelMaterial.LayerColour` is now shared
            by the block textures and the hotbar swatches. A palette in the HUD would be two
            definitions that agree the day they are written and drift the first time either is
            retuned — the `IsCaveAt`/`GenerateTerrain` argument.
      - [x] **A material's label is derived from its identifier's path, not authored in JSON**, and
            this is the one part of M6d-3 that would have been expensive to get wrong. **Mods write
            against a format the moment it exists**, and an authorable name is a name that wants
            translating. **Revisit trigger: the first material whose ID is not a sensible label, or
            the first request to run the game in another language.**
      - [x] **There was no crosshair at all**, in a game whose two verbs are both "what am I
            pointing at". The wireframe target box stood in for one and vanishes the moment you
            point at sky.
      - [x] **The HUD is told and never asked.** Every value is read from the controller that owns
            it. A HUD holding its own copy of the selection would disagree exactly when it
            mattered — the frame after a material ran out and the controller re-selected.
      - [x] **Not created during a profile run**, because the profile reports draw calls and
            primitives and M1d's 2x2x2 question turns on that counter. **It is created in a plain
            headless run**, deliberately, as the cheapest thing that executes the file.
            `--profile` reports 1521 / 165 / 1926 at 404 quads, unchanged to the digit.
      - [x] **The screenshot found two defects the logs could not have**, which is M5c-2's lesson
            arriving in the layer it was written for. The crosshair's vertical bar rendered as a
            line most of the screen tall (`SetAnchorsPreset` plus a `Size` assignment, recomputed
            from the anchors on the next layout pass), and the litres label was clipped off the
            bottom edge.
      - [x] **And one thing that looked like a third defect and was not.** A pale vertical line at
            screen-centre read as a stray wireframe edge; a red crosshair isolated it and a pixel
            sample gave (238, 229, 88) — the yellow cell outline's own back edge, misread against
            green. **The instinct that found the two real defects also manufactured a third**, and
            the pixel sample is what separated them.

#### The one thing the plan deliberately did not cost, and says so

- [ ] **How long a human tolerates between pressing play and standing in their own base.** Every
      number in the plan is throughput. M5c-1 found the metric that mattered was *time to first
      draw* rather than throughput, and found it by sweeping a budget against when the world could
      first be seen. **The equivalent sweep here has no obvious knob**, which is exactly why it is
      the thing to watch for.

#### Surfaced by M6's play, not scheduled

- [ ] **An inventory screen, and slots that stay put — scheduled 2026-09-18: M10g's, as one
      screen; the plan is "The inventory screen — folded into M10g" at the end of
      M10-LADDER-PLAN (eight decisions, all recommended, `E` the key and the unit Matthew's).**
      Matthew, asked whether the ID-order shift bothered him: *"Didn't care but do want to adjust it so slots stay put, when we do the
      inventory screen etc, I was going to allow you to carry more than what is on the hotbar
      maybe?"* **The framing is the design.** Today the hotbar is a *projection* of the inventory
      and is fully determined by it; the moment you can carry more than the row shows, **the
      hotbar becomes an assignment** — which slot holds which material — and that is a different
      object:
      - [ ] It is **state the player authors**, so it must survive a quit: a world-header field
            and a version bump v2 → v3, under M6c-2's policy that **an additive version migrates**.
      - [ ] It **makes slots stay put by construction** rather than by a cleverer sort, so
            `MaterialInSlot`'s revisit trigger is **answered by scheduling, not by reversal**:
            ID order was right for a row that is a projection.
      - [ ] It is where **inventory capacity finally gets somewhere to go**. Unbounded at M6 was
            deliberate, with the recorded cost that M6 cannot say whether volume is a real
            constraint; the recorded triggers were M10 containers and M11 crafting, and **an
            inventory screen is a third and earlier candidate.**
- [x] **Place the lamp in a sealed room — done 2026-09-01, and half the assertion is confounded
      by the fixture.** A lamp on the wall of the sealed room lights it, with falloff into the
      corners, in a room that was **confirmed black** without it. **So `COLOR.g` reaches the
      screen: the sky channel is zero in there by construction, and something is lighting it.**
      - [ ] **The other half — a warm pool rather than a grey one — this frame cannot show, because
            the room is dirt.** `block_tint` is `(1.0, 0.86, 0.66)`, and warm light on brown rock
            looks like white light on brown rock. **The fixture is degenerate in the way the
            assertion cannot see**, which is the category the break-recipe habit exists to catch.
            **The sharp version is one room away: build it in stone** — 2.1 million litres are
            carried — where a warm pool on grey is unambiguous. M5c-2's `--skybrightness=0
            --debuglamp` screenshot already carries this claim; what is missing is the play-level
            confirmation in a room a person built.
      - [ ] **The lamp on that wall did not come out of the inventory** — slot 3 still reads 16 L,
            so it is M5b's `L` debug bind. Identical light either way; noted so nobody reads the
            screenshot as evidence that placing a lamp *from the hotbar* has been exercised.

#### Deliberately not in M6

- [ ] Region files. DESIGN records the decision and its reasoning; nothing here reopens it.
- [ ] Migration between format versions. The version byte is the whole of what M6 owes it.
- [ ] `VACUUM`, save-file compaction, a free list. Revisit when a save file's size is surprising.
- [ ] **Multiple worlds, a world-select screen, or naming.** One world at a fixed path. The moment
      there are two, the ID table's per-world ownership becomes *visible* rather than merely
      correct, and that wants its own pass.
- [ ] Persisting light, or the mesh — the mesh is larger than the voxels it came from (~46 KiB a
      drawn chunk against ~2 KB) and cheaper to rebuild than to read.
- [ ] **Dropped items.** DESIGN puts them here and is explicit that Godot physics is fine for
      them, but they are an entity system and M6 has no entities.
- [ ] **Undo.** A sandbox invites it and it is a different data structure — a journal, not a
      snapshot. Cheaper to add on top of a working save than to build into one.
- [ ] The entangled miner becoming real content — recorded at M5d-2 as wanting its own pass,
      because *reach is the player's, not the tool's* is locked twice.

---

## Phase 2 — The loop

### M7 — Rain ✅ **built and played 2026-09-02. All four parts, eight sub-parts, five play questions.**

Planned into four parts in [docs/M7-PLAN.md](docs/M7-PLAN.md). **Read it, and read
[docs/SCALE.md](docs/SCALE.md) before it** — that file was written for this milestone.

**The structural point: a watershed is the first thing in this project that cannot be made exact
by choosing a big enough neighbourhood.** Light was the last thing that was not a function of one
chunk, and arithmetic saved it — 15 m of reach against a 16 m margin. Where water goes from a
column depends on terrain arbitrarily far away and there is no attenuation distance at all.

**What makes it tractable is that the input is a pure function rather than the world.**
`ISurfaceHeights.SurfaceHeightAt` is analytic, seeded, needs no chunk loaded and already has a
test double. So the analysis needs **no streaming, no margin, no barrier and no stage** — it is
not a chunk-shaped computation, and the instinct to make it one is the thing to resist.

**Second structural change: the world gains a clock.** Nothing here has ever needed time —
worldgen is timeless, light is a fixpoint, physics takes `dt` and remembers nothing. M7 needs one
and so does everything after it. Additive, so header **v2 → v3 migrates**.

#### The decisions settled before any code, three of them by Matthew on 2026-09-01

- [x] **M7 renders standing water — Tier A gets a surface.** Two arguments: DESIGN's Tier B says
      settled cells *merge into a Tier A body*, so this renderer is needed at M8 anyway and is
      brought forward rather than duplicated; and without it M7 has no play verdict at all.
      **M5b already ran that experiment** — a correct light grid that did not glow, unjudgeable
      until M5c-2.
      - [x] **Known ugly part, accepted up front: the shoreline.** A surface on the 4 m grid
            against 0.25 m terrain stair-steps at the water's edge, which is the most-looked-at
            part of a pond. **M8's Tier B replaces it**, so refining the shore now is thrown-away
            work. Lever if the play cannot bear it: re-sample the boundary ring at 1 m.
            **Halved to 2 m on 2026-09-10** at Matthew's request — one constant, the analysis
            22.5 ms reference-equivalent for the 512 m window (`--watershed`, 256 samples a side,
            130 ms in the Debug game on a worker), 391 basins in the spawn's window against 255.
            - [x] **The lever was built the same night, cheaper than named: the terrain clips the
                  quad.** Matthew's rule — a dry sample beside a wet one draws the wet one's plane
                  and raised voxels cover it. `WaterSurface.DilateShore`, `--shore=0` the control.
                  Voxel-exact in the A/B on a copy of the played world (`C:/tmp/shore-before.png`,
                  `shore-after.png`), 1,667 → 2,389 quads, 6,714 ring samples, rebuild cost
                  unchanged. **Only into the same lake**: the first version dilated across divides
                  and hung a slab over a saddle, because the played lakes stand at 108% of capacity
                  and the far slope was under the level. A sliver can still hang where a same-lake
                  cell's footprint straddles a crest — bounded to one cell, and the same error a wet
                  cell has always had. The whole-catchment plane (no comparison at all) was
                  considered and refused: the clipping terrain is the *dug* terrain and the level
                  is the seed's, so every excavation below the lake's level in the catchment would
                  show water.
            - [x] **Played and accepted, 2026-09-10** — *"looks awesome"* on the played world in
                  rain: 2 m, the ring and the 1 cm bias ship. The 4 m arm was not run (the cell is
                  a constant, so `--waterwindow=128` at 2 m is a 256 m window, not 4 m) and is
                  deliberately not made a flag: the header's lattice check discards the stored
                  lakes on every change, so an A/B on the played world would wipe them twice.
                  **Revisit trigger: the analysis cost being felt** — 130 ms Debug on a worker per
                  window move — at which point 4 m is one constant and a scratch world.
            - [ ] **Every world written at 4 m opens with its lakes discarded, once**, with the
                  `M7d: the watershed lattice has changed` warning; they refill. Tier B water in
                  the mine is in the fluid panels and is untouched.
- [x] **Rainfall is a seeded weather cycle, and the weather is analytic.** Rain rate is a pure
      function of (seed, world time) — episodes hashed through `splitmix64`, so rainfall over any
      interval is a closed-form sum. **That is what makes integrate-forward exact rather than
      approximate**, it is server-authoritative for free per non-negotiable 1, and it is testable
      without a clock. `--rain=<intensity>` overrides it, the seam `--lightbudget` occupies.
      - [x] **Time advances only while the world is open.** Quitting for a week must not flood the
            map, and an unbounded integration interval is the other half of the same problem.
            **Revisit trigger: the first mechanic that wants the world to have moved on while you
            were away** — crops, or an offline pump.
- [x] **The watershed grid was 4 m, and is 2 m since 2026-09-10 (see the shoreline item above)** — half a chunk, 2×2 samples per chunk column, 16 m² of
      catchment each. A 30 m pond is ~56 samples; a 1 km window is 62,500. **It sits between the
      1 m light cell and the 8 m chunk column deliberately, so it borrows neither one's revisit
      trigger** — the `BrushCell`-is-not-`LightCellPos` argument from M6c-1.
      - [ ] **It is format-affecting**, because basin identity derives from it. Store
            `WatershedCellMetres` in the header at v3, and **on mismatch discard stored volumes
            rather than refuse the world** — they refill in minutes of rain, and refusing a world
            over a derived quantity is the worse failure.
- [x] **A basin is identified by its pit, in world coordinates — never by an array index.** The
      window is player-centred, so every index renumbers when it moves and **persisted volume
      keyed on an index silently reattaches to a different basin**: the lake drains, the valley
      next door floods, nothing fails anywhere. Priority-flood already yields the pit, which is a
      property of the terrain. **Non-negotiable 2's argument arriving somewhere nobody expected
      to need it.**
      - [ ] **Two pits can tie on elevation**, so the tie-break must be a total order on the world
            coordinate — not "whichever the heap popped first", or the ID moves between runs on
            identical terrain.
- [x] **The window edge is an outlet, not a wall**, because the failure direction is the safe one:
      under-filling means a lake that should exist does not, over-filling means water arriving
      where the player built. It also keeps **rain in == stored + spilled** an exact assertion.
      - [ ] **Recorded cost: a lake wider than the window fills only to the window's rim.**
            **Revisit trigger: a body of water whose far shore the player can see.** At 1,024 m
            the window is seven times the r16 view distance, so this should not fire; if it does,
            the fix is a coarse second tier rather than a bigger window.
- [x] **Edits do not change the watershed at M7, and the seam is named.** The analysis reads
      unedited terrain, so a pit you dug does not fill. That is not a hole in the core loop:
      **the mechanism that carries water into an excavation is Tier B, which is M8**, and shipping
      half of it here would produce water teleporting into a hole rather than running into it.
      **`SkyColumn` already holds edited height per voxel column and is already persisted** — that
      is what the follow-up reads.
- [x] **The code lives in `Overburden.Core/Hydrology/`**, no Godot, per non-negotiable 1.
      `Overburden.Persistence` gains one table.

#### M7a — The watershed ← **split in two 2026-09-01. M7a-1 is done; M7a-2 is the merge tree.**

Split while building, on the evidence rather than up front: the flood and the curves are one
self-contained thing that the plan's own first job (the measurement) needed in order to exist, and
the merge tree is a second thing with its own hazard — see the two-cycle below.

##### M7a-1 — The flood, the curves and the measurement ✅ **done 2026-09-01, 756 tests passing**

- [x] Priority-flood (Barnes, Lehman & Mulla 2014) over the window: pits, saddles, catchments and
      basin labels. `Overburden.Core/Hydrology/` — `HeightField`, `Watershed`, `Basin`,
      `VolumeCurve`. No Godot, no chunks, no streaming.
- [x] **The measurement, taken first as the plan asked.** ⚠ **Corrected at M7a-2: the figure
      below is a *reference-machine* number and the machine varies by 2.5x.** As first published:
      a 1,024 m window at **19.9 ms**, 3.64 sampling and 16.30 flooding. The same binary hours
      later: **43 ms**, with the worldgen control at 1.16 ms/chunk against M2d-2's recorded 0.47 —
      a 2.48x factor that accounts for all of it. **Including M7a-2's tree the honest pair is
      66.4 ms measured / 26.8 ms reference-equivalent.** The decision is unchanged — the analysis
      runs on a worker once per window move, well inside the plan's 200 ms rethink line — but the
      margin is **3x, not the 10x that 19.9 against 50 implied.** **The bench now prints the
      control above its table**, so no future figure needs this footnote.
      - [x] **The estimate was inside its range and wrong about which half.** It assumed sampling
            at 0.1–0.2 µs dominating; measured, **sampling is 55 ns/sample** and **the flood is 82%
            of the total**. The range was right for a reason that was not — and the *lever* moved,
            which is what matters: `SurfaceHeightAt` is not where the time is.
      - [x] **A JIT warm-up artefact produced a fake finding first.** One warm-up pass left small
            windows in tier-0 while the 1 km flood was promoted mid-run by OSR, reporting 47.2 ms
            and a per-sample cost that fell with window size — which reads as memory locality and
            is a believable thing to write down. Warming up by **call count** moved 1 km from 47.2
            to 19.9 ms. **The headless-frame-time family again**; the tell was a discontinuity
            between two adjacent rows rather than a trend across them.
- [x] **Flat ground was the defect that mattered, and a fixture found it rather than theory.** The
      corner of a flat shelf has no strictly lower neighbour, so a per-cell sweep declares it a
      pit — and voxel heights sampled every 4 m tie constantly, so real terrain would fill with
      basins that hold nothing, each stealing catchment from the real one. Fixed by sweeping a
      **tier** of equal (filled, height) at a time, seeding it from lower ground and spreading the
      label across the flat: Barnes' flat resolution in the one place this needs it.
- [x] **The sweep needs a global sort; the flood's own pop order is not enough.** The flood enters
      a depression from the rim inward, so labelling during it makes the first cell of a descending
      corridor the pit and the real pit never becomes one.
- [x] **Ten break-recipes, eight caught, two documentation corrections.** The saddle's explicit
      tie-break is belt and braces (row-major scan is already translation-stable), and the
      `key < own` guard in the seeding pass is unreachable — **which corrected a comment written an
      hour earlier** claiming it had become load-bearing. M4a's zeroable-epsilon category, fourth
      and fifth outings.
- [x] **Two independent oracles, and they are the two that matter.** A stepped tank whose volume is
      arithmetic done on paper, and the filled heights recomputed by repeated flood fill at rising
      thresholds — a different algorithm, hopeless on a real window and exactly right on a
      twelve-sample one.

##### M7a-2 — The merge tree ✅ **done 2026-09-01, 764 tests passing**

- [x] **The two-cycle test flipped, which is what this part was for.** `MergeTree` sorts every
      crossing and merges with union-find; `BasinWater` routes overflow strictly upward, so it
      cannot loop. The same terrain that ping-pongs at leaf level now fills, spills into its
      sibling, rises over the ridge as one lake and leaves the world.
- [x] **Two defects, and the second was found by a test written to plug a hole left by the first
      battery.**
      - [x] **Two components that both already drain to the window edge met at a crossing and one
            was re-closed at the higher height.** Every pair meets somewhere, so this was not an
            edge case — it silently overwrote a spill height settled long before, and the symptom
            would be a lake stopping at a height nothing explains. Caught by the test that says a
            leaf's spill height is the saddle the watershed already found.
      - [x] **The rolled-up free capacity went stale after a drop that fit in its own basin**, so a
            merged lake would report a level computed from a volume last updated at the previous
            overflow. **A new variety: the green recipe was about the roll-up, the test written to
            plug it was about the roll-up, and the defect it found was somewhere else.** A green
            recipe means *no test here*, and what the missing test catches need not be the mutation
            that revealed it was missing.
- [x] **Water is held in slabs, not piles** — a leaf up to its own rim, a composite only the volume
      between its children's rims and its own. A subtree's water is then the sum of its nodes and
      "how much is in this lake" is one subtraction.
- [x] **Pouring fills the children before the parent's own slab, and that is physics rather than an
      optimisation.** Fill the parent first and a brimming basin's overflow stands on the ground
      between two lakes while one is still half empty.
- [x] **Brim-full is not merged.** Two siblings are separate lakes until the water stands *above*
      the ridge — which is exactly when the parent's slab starts to hold something.
- [x] **Eager composite curves, and the lazy alternative is retired rather than deferred.**
      Measured **0.8 MB at a 1 km window**, 5.9 MB at 2 km. Sixth named fallback dissolved by
      measuring the thing it was afraid of.
- [x] **Eleven break-recipes, ten caught.** The green one is a documentation correction: setting a
      composite's children's parent before closing them is belt and braces, because `drained` is
      only read through `Find`, which returns roots.
- [x] **The volume→level curve: sorted elevations plus a prefix sum**, so `volume(L)` is a binary
      search and `level(V)` is the same search inverted. Exact, monotonic, invertible — the level
      is *solved, not simulated*. Capped at the saddle.
- [x] Tests, invariants rather than a second flood to get wrong. Built at M7a-1:
      `volume(level(v)) == v` both ways; a stepped tank checked against arithmetic done on paper;
      a conic bowl within a few per cent of `πr²h/3`, the tolerance being discretisation rather
      than slack; filled ≥ ground everywhere; absurd volume pinning at the saddle; a tilted plane
      with no basins; **the same pits through a translated window**; and the flood checked against
      a brute-force minimax that shares no code with it.
- [ ] **Still open from M7a-1, and named rather than assumed:** the spill *target*'s stability
      across a window move is asserted only for the case where it is `Outlet`. A fixture with two
      equal crossings to two different basins would pin the rest. Cheap, and it did not block.

#### M7b — The clock, the weather and the accumulator ← **split in two. M7b-1 done; M7b-2 is persistence.**

##### M7b-1 — The clock, the weather and the accumulator ✅ **done 2026-09-01, 778 tests passing**

- [x] **`WorldClock`, integer milliseconds**, not a `double` of seconds — a float clock drifts and
      this one is the input to a determinism claim. The fraction of a frame is carried, so 60 Hz
      and 144 Hz reach the same time.
- [x] `Weather.RateAt(seed, t)` and `DepthNanometresOver(seed, t0, t1)`, the second closed form
      over the episodes it spans.
- [x] **The unit chain is the design, chosen to make one multiplication exact.** A rate is
      nanometres per millisecond (= µm/s = 3.6 mm/hour); depth is rate × milliseconds; and **a
      nanometre of rain over a square metre is exactly one microlitre**, so the volume arriving in
      a basin is an exact integer. Nothing rounds, which is what lets the oracle assert equality.
- [x] **The sub-litre remainder is carried, not dropped.** Drop it and a drizzle rounds to zero
      litres every step forever, so a basin never fills while the same hour integrated in one call
      fills it properly.
- [x] Overflow walks the merge tree; surplus at the window edge leaves and is counted.
- [x] **The oracle passes with no tolerance: an hour in one call equals 3,600 calls of one second**,
      basin for basin, remainder for remainder, spill for spill.
- [x] **The pacing finding, which M7d has to react to: at real rainfall the median basin fills in
      35 minutes of the heaviest weather this world can produce**, and with 55% of episodes dry the
      honest expectation is hours. A filling pond rises well under a millimetre a minute — nobody
      can judge that.
      - [x] **`rainfallScale` is the honest knob**, and it is a scale rather than a fudged rate so
            that `Weather` goes on stating real intensities for M7c's wet term and the overcast.
            **M7d is where a person picks the number**, against the bench's new `fill min` column.
            Matthew's *"a few minutes to fill the lower levels of a large cave"* implies 10–30x —
            or it was about caves, which are M8 and much smaller.
- [x] **Ten break-recipes, ten caught — after the green one exposed a fixture that could not
      fail.** Reporting only the last call's spill left the suite green, because **at real rainfall
      none of the fixtures ever overflows**, so every assertion about spilled water compared zero
      with zero. Fixed with a fixture that drowns on purpose and asserts that it did.
- [x] **The machine control validated itself**: 2.48x slower and 66.4 ms, then 1.08x and 29.9 ms,
      which normalise to 26.8 and 27.7 — agreement to 3% across a machine that had changed speed
      by a factor of two.

##### M7b-2 — Persistence ✅ **done 2026-09-02, 797 tests passing**

- [x] A `basins` table keyed `(pitX, pitZ)`; zero-volume basins are simply absent. SQLite schema
      version 1 → 2; an older file gains the table on open.
- [x] **`BasinLedger` is the structural point: a `BasinWater` only ever holds what is inside the
      analysis window.** Walk a kilometre and the lake you left has no representation in memory at
      all, so the persistent thing has to be a map from *pit coordinate* to litres that outlives
      every analysis. **Second time M7a-1's identity decision has paid for itself.**
- [x] **Capture is exact, and the reason is a property rather than a mechanism.** Pouring
      `CaptureByBasin`'s result back into a fresh water over the same tree reproduces the state
      node for node, because fill-and-spill is **order-independent** — where water rests depends on
      how much entered at each basin, not on the order.
      - [x] **The battery proves the attribution is arbitrary rather than the comment claiming
            it:** crediting composites to the rightmost basin instead of the leftmost leaves the
            suite green, while dropping composite slabs fails two tests.
- [x] **Absorb replaces, it does not add** — otherwise a lake doubles every time the player crosses
      the same ground twice, which only shows up in a long session and reads as generous weather.
- [x] **`WriteBasins` replaces the table rather than merging, and the cost is stated.** Tens of
      thousands of rows for a well-travelled world. **Revisit trigger: the basin write becomes
      visible next to M6b-3's 13 ms autosave**; the fix is dirty-tracking in the ledger.
- [x] **Ten break-recipes, nine caught.**
- [ ] **The oracle, and it is the same category as M5's:** integrating **600 s in one call must
      equal 600 calls of one second, to the litre**. No tolerance — integers and a closed form are
      either identical or one is wrong. **The assertion and the feature are the same statement**,
      because that property *is* "flew away mid-fill and came back", and it is the cash value of
      [slower than realtime is accuracy](#slower-than-realtime-is-accuracy-not-approximation--matthew-2026-09-01).
- [x] Also: store round trip; a changed `WatershedCellMetres` discarding rather than throwing;
      header v3 migrating v1 and v2; a negative clock refused at write and at read.
- [x] **Discharged at M7c-1: the clock does not advance while the world is closed.** Core cannot
      test it — `WorldClock` only moves when something calls `Advance` — and the demonstration is a
      pair of sessions: one ends at t ≈ 6 s, the next resumes at t = 11 s five minutes of wall
      clock later.

#### M7c — Rain you can see ✅ **split in two 2026-09-02; both parts built. Splashes cut — see M7c-2.**

Split on the seam M5c was split on — **what a headless run can verify and what needs a renderer.**
M7-PLAN says "headless cannot verify any of this", which is true of the particles and false of the
wet term: the zero-remesh assertion and the clock are both headless facts, and putting them in the
same part as the billboards would have hidden them behind a screenshot.

##### M7c-1 — The clock, the wet term and the overcast ✅ **done 2026-09-02, 804 tests passing**

- [x] **`WorldClock` wired into the session**, advanced from `_Process`'s `delta` and from nowhere
      else, restored from header v3 at open and written back on save.
      - [x] **The demonstration M7b-2 said was still owed, and it is a pair of sessions**: one ends
            at t ≈ 6 s, the next resumes at t = 11 s five minutes of wall clock later. *Time
            advances only while the world is open*, observed rather than asserted — Core could
            never test it, because `WorldClock` only moves when something calls `Advance`.
- [x] **The wet-surface term is one uniform and zero remeshes, and it is now printed rather than
      argued**: `weather 0 edit remesh(es) / 0 relight remesh(es) during the measured window — wet
      ranged 0.00..0.67 over 1380 s of world time`. **Where M5's bill gets collected** — DESIGN's
      reason for keeping the two light channels separate was this exact term.
      - [x] **A difference, not a total, and printed with its denominator.** The fill is still
            relighting through the warm-up, so a session total would read in the hundreds; and a
            zero from a run that stayed dry is a detector that never fired.
      - [x] **`--clockscale` is what makes fifteen seconds contain a storm.** Weather is a pure
            function of time, so scaling the clock shows a whole episode without touching the
            weather. **Deliberately not M7d's `rainfallScale`**, which leaves the sky honest and
            speeds the water up; a run that uses this one is not comparable with one that does not.
- [x] **`Weather.WetnessAt` and `Weather.OvercastAt` are box filters over `DepthNanometresOver`.**
      The one primitive the milestone's exactness rests on is the only arithmetic in either, so
      there is no second model of "recent rainfall" to drift from the first.
      - [x] **The soak window is exactly one episode, which makes the answer statable**: on an
            episode boundary wetness *is* `rate / maxRate`, a rational number a test asserts. The
            tightest available assertion about the window's length.
      - [x] **The overcast window straddles now** — back one soak window, forward half an episode —
            so the sky gathers before the first drop and clears as the ground dries. Two tests
            separate the halves.
      - [x] **A new world reads dry in a downpour for two minutes and that is correct.** The window
            clamps at the start of time and the denominator deliberately does not. Normalising by
            the clamped span reports saturated ground one second after a world is created.
      - [x] **This is where the integer rule stops, deliberately** — these feed a shader uniform,
            are never stored, summed or compared, and an `int` here would be exactness cargo-culted
            onto a look term. Said in the source, because the file's ethos argues the other way.
      - [x] **Eight break-recipes, eight caught.**
- [x] **Overcast dims through `sky_brightness` by multiplying `--skybrightness` rather than
      replacing it**, which keeps M5c-2's A/B alive. **Verified rather than assumed**:
      `--skybrightness=0` still renders the terrain pure black against a sky that is still there.
- [x] **M7c is where the view-space `NORMAL` question stops being cosmetic — and it was a real
      bug.** `face_tint` read `NORMAL` in `fragment()`, which in Godot 4 is view space, so the fake
      directional shading rotated with the player. Fixed with a `varying` set from `vertex()`'s
      `NORMAL`, which for a chunk instance **is** the world normal — the transform is a pure
      translation, so it costs no matrix multiply.
      - [x] Measured: `riser face` (69,130,64) in both arms at yaw 0, (59,128,54) against
            (53,116,50) at yaw 40. **Identical facing the default direction, diverging once you
            turn** — the bug stated as a measurement.
      - [ ] **Which look Matthew prefers is still his.** Correctness settled and world space
            shipped; `--viewnormal=1` is the other arm and `--yaw=` is what makes it comparable,
            because a view-dependent artefact is invisible in one picture by definition.
- [x] **The sky did not dim, and three plausible levers all silently did nothing.**
      `Environment.BackgroundEnergyMultiplier`, `ProceduralSkyMaterial.EnergyMultiplier` and
      scaling the dome's four colours were each confirmed *set* and each left the sky pixels
      bit-identical to the clear arm — (200, 210, 225) in both.
      - [x] **The cause was a note already in CLAUDE.md, one milestone old**: M5d left the
            environment's fog on for the sky so the dome's ground hemisphere would not band the
            horizon — so **most of the sky you can see is the fog**, at a fixed colour. Dimming
            `Environment.FogLightColor` moves it to (83, 90, 101).
      - [x] **Eyeballing a screenshot is a measurement too, second outing.** M6d-3's was a false
            positive; this is the false negative of the same shape — a picture that looked right
            while half the intended change did nothing. Sample named points; do not admire.

##### M7c-2 — The rain itself ✅ **done 2026-09-02, splashes cut — 804 tests passing**

- [x] **Camera-locked box of GPU-instanced, velocity-stretched billboards, one draw call — and the
      draw call is measured rather than claimed.** Windowed r4h/2v at 8 m/s against `--norain`:
      **85 draw calls against 84, 112,332 primitives against 64,332** — one call, and 48,000
      primitives which is exactly 24,000 drops × 2 triangles.
      - [x] **No measurable sim-thread cost**: `update ms` mean 2.696 with rain against 2.872
            without, `main` 38.38% against 40.93%, frame ms identical. The rain arm is *lower*,
            which is noise around zero — everything a drop does is in the process shader.
      - [x] **Cylindrical rather than spherical billboarding**, or a falling drop tips over as you
            look down at it and the streaks stop being parallel to their own fall.
      - [x] **A near fade, found in the very first screenshot.** The box is centred on the camera,
            so some drop is always beside the eye — a metre-long quad a few centimetres away, as a
            bright wedge over a quarter of the screen. Unavoidable rather than a spawn bug.
- [x] **The occlusion kill is a sky-height texture** built from `SkyColumn`, so it is *edited*
      height and rain follows a shaft rather than a heightmap.
      - [x] **0.5 m resolution as recommended, and each texel takes the minimum of its 2×2 voxel
            columns** rather than point-sampling one. Point sampling works for a three-voxel bore
            by a parity argument that breaks silently the first time the resolution moves; the
            minimum is a guarantee that does not.
      - [x] **The plan's ~0.25 ms estimate was ten times out.** At the recommended 128 m window the
            refill measured **2.5 ms mean / 3.6 ms worst** on the sim thread, next to `viewer ms`'s
            4.7 ms on the same crossing — so it roughly doubled the cost of a chunk crossing.
      - [x] **The window is now 64 m and measures 0.98 ms / 1.60 ms.** Still 4× the estimate. 128 m
            existed only so the box could be retuned without touching the field; 64 m clears the
            48 m box by 8 m a side, and **the resolution the plan actually argued is untouched**.
            Bonus: the "unknown" share at r4 went 34% → **0%**, because a 64 m window fits inside
            the loaded region where a 128 m one does not.
      - [x] **The field prints its own height range, and that check was not optional.** If the `Rf`
            texture were clamped anywhere in the path the kill would be a flat plane — and *rain
            stopping at a flat plane looks exactly like rain stopping at the ground* from anywhere
            the ground is a metre below you. Every screenshot so far would have passed.
            `ground -3.5..5.0 m` is what says it is a terrain. **Third outing for "eyeballing a
            screenshot is a measurement too"**, and the first where the doubt arrived from an
            unrelated failure.
      - [x] **`ChunkPipeline.SkyHeightChanges` is the dig trigger, built with its consumer.** It
            counts height *moves* rather than voxel writes, so a tunnel dug underground — most of
            mining — rebuilds nothing at all. Rebuilds are reported split by cause
            (`24 moved / 0 dug`) because a crossing is bookkeeping and a dig is the feature.
      - [x] **The counter's own first reading was wrong**: `_skyChangesAtRebuild` started at −1, so
            every run reported "1 dug" before anything had been dug — the exact false positive it
            exists to rule out.
      - [x] **Guarded behind the rain existing**, or a moving headless profile would carry a
            millisecond per crossing that no run since M2d has carried.
- [ ] **Splash particles: NOT DELIVERED, and cut rather than shipped dead.** Two implementations,
      neither drew anything that survived scrutiny. A particle system that draws nothing is
      indistinguishable from one that is working and subtle, so leaving it in would have read as
      "splashes exist, they need tuning". **Each finding below is from counting pixels in a
      forced-colour screenshot, not from looking:**
      - [x] The splash draw pass and process shader **work** — 85 pixels self-emitting with the
            sub-emitter link cut. Neither half was the problem.
      - [x] **The sub-emitter link suppresses self-emission and delivers nothing in its place: 0
            pixels**, with the emitter hitting the ground thousands of times a second.
      - [x] Re-parenting the sub-emitter as a **child** of the emitter got particles out of it but
            **all at a single point**, so `FLAG_EMIT_POSITION` was still not arriving.
            (`EMIT_FLAG_POSITION`, the name that reads naturally, does not exist — and that one is
            a compile error rather than a silence.)
      - [x] **A `texture()` call inside a particle `start()` silently yields particles that never
            appear.** Identical shader with the ground looked up in `start()`: 0 pixels; reading a
            constant instead: drew immediately. Nothing logged, nothing fails to compile. **The
            most valuable finding here**, and why any future attempt must look up in `process()`.
      - [ ] Moving the lookup to `process()` did not on its own bring them back, so **at least one
            more thing is wrong**. That is where it was left.
      - [ ] **The alternative design is the right shape and is written up in the removed shader's
            header**: a second emitter placing droplets on the same field rather than at the kill
            point. The difference is invisible — nobody follows a drop at 22 m/s to its own splash
            — and the one place it would matter is preserved, because the same field puts splashes
            at the bottom of a dug shaft rather than on the roof around it.
- [x] **Screenshot seams**: `--pitch=<degrees>` joins M7c-1's `--yaw`, because a shot of rain going
      into a shaft has to look down it. `--norain` is the arm that priced the particles.
- [ ] **A positive shaft photograph was not obtained.** Underground shots correctly show *no* rain,
      which is the occlusion working in its negative form; a picture of rain pouring into a hole
      needs a narrow shaft and a camera outside it, and the entangled miner digs broad chambers
      while a reopened player falls to the bottom of whatever it dug. **This is M7d's play
      question rather than a test** — a person standing at their own mine entrance answers it in a
      second.

#### M7d — Standing water, measurement and play ← **split in two 2026-09-02. Both parts built; the play is Matthew's and is all that is left of M7.**

Split on the seam M5c and M7c were split on: what a headless run can verify, and what needs a
renderer and a person.

##### M7d-1 — The wiring and the measurement ✅ **done 2026-09-02, 814 tests passing**

- [x] **`Overburden.Core/Hydrology/WaterSystem.cs`** owns the analysed window, the water in it and
      the ledger that outlives it. Godot-free, so the threading policy lives where the platform is.
- [x] **A lake survives a quit, to the litre.** Session one quits at 9,404,288 L; session two opens
      and reports 9,404,288 L standing across 346 banked basins. The in-memory twin is the suite's
      oracle, `WalkingAwayAndBackLosesNoWater` — **every window move is a chance to lose a lake**,
      so the assertion and the feature are the same statement.
- [x] **M7a's analysis estimate is confirmed exactly.** Release bench: 1024 m at 64.97 ms against a
      2.46x machine control = **26.4 ms reference-equivalent**, against M7a's recorded 27. The
      shipped window is **512 m — 13.63 ms Release, 5.5 ms reference-equivalent**, on a worker once
      every ~128 m of travel.
      - [x] **New fact: the Debug penalty is ~3.9x warm** (53.1 ms in-game against 13.63 Release),
            and ~102 ms cold. The game builds Debug, so a profile figure is not a bench figure —
            the same trap that has now caught worldgen (3x), the light derive (5.9x) and this.
- [x] **The water costs nothing measurable on the sim thread**, against `--nowater` at 40 m/s:
      `update ms` 6.107 against 6.064, `viewer ms` 10.845 against 11.125, `main` 83.55% against
      84.19%. The water arm is *lower* on two of three. A tick is one multiplication per basin.
- [x] **`rainfallScale` was bounded by numbers and placed by a person: x40, halved to x20.** The
      bench says the median basin in a 512 m window fills in **32 minutes of the heaviest possible
      weather** unscaled; the game says a window reaches **10.2% of leaf capacity in 40 minutes of
      ordinary weather**, so a full basin is ~6.5 hours. At x40 that is ~10 minutes, and measured
      in real time from a 36 mm/h downpour the window took **25.3% of capacity in 35 wall-clock
      seconds**.
      - [x] **Moved to x20 by the play, 2026-09-02**: *"a little too fast, um like you could half
            the fill speed, it would still work as well I reckon."* **The arithmetic could say the
            scale was somewhere in the tens and could not say where** — this is the one M7 number
            no measurement was ever going to settle, which is why M7d-2's play existed. A basin
            now fills in roughly twenty minutes. **Revisit trigger: a world whose basins are
            deeper than the spawn's** — the play that picked it was on gently terraced terrain
            holding shallow ponds, and a lake ten times the volume is ten times the wait.
            `--rainscale=` stays the sweep.
- [x] **A first-draw regression that was the machine, and the control is what said so.** 7.2 s
      against 3.4 s earlier the same day reads exactly like the analysis stealing thread-pool time
      from the fill — and **the `--nowater` control ran slower still, at 8.65 s**. **First time the
      machine-variance finding has been applied to a streaming figure rather than a bench one**: a
      fill time quoted without a same-session control is not a measurement.
- [x] **The analysis is a plain `Task`, not a job on the scheduler.** M7-PLAN is explicit that a
      watershed is not chunk-shaped and that the instinct to make it one is the thing to resist:
      `JobPriority` ranks by distance from the viewer in chunks, which says nothing about a window
      already centred on the viewer, and there is no chunk to cancel it with.
- [x] **Absorb before seed, always**; the accumulator starts at what has been **delivered** rather
      than at now; and a reopened world starts at its own clock, or the first window to land pours
      the whole of the world's history into basins the ledger has already filled.
- [x] **The window origin has a whole step of hysteresis** — it snaps to quarter-spans — so a player
      on a boundary cannot re-analyse every time they shift their weight.
- [x] **Ten break-recipes, eight caught on the first pass and all ten after one fixture change.**
      **Both misses were the same trap and both were real defects**: the fixture rained hard enough
      to fill every basin, so "the move dropped a step's rain" and "the move delivered it twice"
      produced the same brim-full number as correct code. Fourth outing for that failure here.
      - [x] **Rescaling then exposed a genuine cost the saturated fixture had hidden**: a window
            move discards `BasinRainfall`'s pending **sub-litre remainder**, because each move
            builds a fresh accumulator. Bounded at under a litre per basin per move, **asserted as
            a bound rather than waved at**, and stated in the source. **Revisit trigger: anything
            that re-analyses often**; the fix is to carry the remainder keyed by pit exactly as the
            ledger carries litres.

##### M7d-2 — The surface ✅ **built 2026-09-02, 814 tests passing. The play below is Matthew's.**

- [x] **One flat translucent surface per basin with volume, at the solved level.**
      `Overburden.Game/Rendering/WaterSurface.cs` sweeps the analysis grid and emits a horizontal
      quad wherever a terrain sample sits below its basin's level. **Nothing iterates water** — it
      is one number per basin against one height per sample, which is DESIGN's *solved, not
      simulated* reduced to a sweep and a comparison.
      - [x] Measured: **739 quads over 255 basins, 0.67 ms to rebuild**, 83 rebuilds in 15 s;
            `update ms` 2.709 mean, `viewer ms` 4.831, `main` 38.34%, **85 draw calls**.
      - [x] **Runs merged along X on `(basin, level)`, not on wetness** — two neighbouring basins at
            different levels are two lakes, and a quad spanning both is a surface at neither.
            Removed roughly three quarters of the quads for one comparison per sample.
      - [x] **Rebuilt on a 0.25 s timer, picked against how fast water can move** rather than
            against a frame budget: at the shipped scale a level moves well under a millimetre
            between rebuilds, which is below a pixel from anywhere you can see the pond.
      - [x] **Depth tested but not written.** Tested so a ridge hides a pond behind it; not written
            so the terrain under the water still draws and a shallow pond reads as shallow. One
            plane per basin needs no sorting inside itself. **M8 owns the real transparent pass.**
- [x] **A wide hero shot of a lake was not obtained, and the reason is this world rather than the
      code.** The spawn's terrain is gently terraced, so its basins are shallow — 346 of them
      holding 724 quads between them, a field of puddles rather than a lake. Flooding to 866% of
      leaf capacity merged them, and the second limit bit: **the default view radius is 32 m
      against a 512 m analysis window**, so most of the water is past the fog. **The play has
      neither limit** — a person walks to a valley and looks at it.

##### M7d-2's play ✅ **done 2026-09-02 — five questions, five answers, and M7 with them**

- [ ] One flat translucent surface per basin with volume, at the solved level. Depth-write off,
      no sorting inside a basin because it is one plane. **M8 owns the real transparent pass.**
      **The levels are already solved and `WaterSystem.LevelAt` returns them — nothing draws them**,
      which is exactly where M5b stood with a light grid that did not glow.
- [x] **Sighted 2026-09-02, and it is a sixth finding rather than one of the five**: *"the surface
      level was rising in sealed ponds like, but where I had tunnelled into the earth, it did not
      fall in."* **Not a defect — it is the boundary two items below, met by a person.** An
      excavation is invisible to the analysis in *both* directions: water cannot get into a tunnel
      under a pond, and a cut through a basin's rim does not drain it either. **The half that reads
      as broken rather than as missing is not yet confirmed by eye** — an open pit dug *inside* a
      basin below the solved level still emits its quad from the unedited height, so the water
      plane hangs in air over the hole with the pit walls drawing through it. **If the play sees
      that, it is the one part of this worth a cosmetic guard before M8; if it does not, nothing
      here moves.** The five questions below stand unanswered.
- [ ] Play, the questions no test answers:
      - [x] **Answered 2026-09-02: yes.** *"Looks like rain to me when it's falling from the
            sky."* No knob was reached for — `drop_length`, `drop_width`, `alpha`, the near-fade
            and `OvercastDimming` all ship as built. **The hedge in the wording is unexamined
            rather than a reported fault**: the verdict is on rain seen falling from open sky,
            and the guide's other half of question 1 — whether you still catch a drop right at
            the eye — was not separately reported either way.
      - [x] **Answered 2026-09-02: yes, it stops at the mine entrance.** *"It does stop at the
            mine entrance."* **DESIGN's *the detail people notice*, noticed** — and it is the
            one thing in M7c-2 that no screenshot could establish, because underground shots
            show the negative case and a picture of rain pouring *into* a hole needs a narrow
            shaft and a camera outside it. **So the whole M7c-2 occlusion chain is confirmed end
            to end by a person**: `SkyHeightChanges` → field rebuild → `Rf` texture → the kill in
            the process shader. The 64 m window and 0.5 m resolution stand as shipped.
      - [x] **Answered by silence 2026-09-02, which is this question's own stated criterion.**
            The guide says outright: *if the play does not notice, the four findings stay on the
            shelf.* Across a play that reported on rain, occlusion, standing water, fill pacing
            and the shoreline, **splashes were never mentioned.** **Recorded as silence rather
            than as evidence**, per the habit this project has broken itself on before — but the
            criterion was written in advance, so it is silence the question asked for. **Revisit
            trigger: the first time rain landing on dry-looking ground is remarked on.** The
            four M7c-2 findings stay on the shelf, and the most useful is that a `texture()`
            call inside a particle `start()` silently yields particles that never appear.
      - [x] **Re-tested and settled properly the same day, and it is no longer the soft one.**
            The first null came from an observer who did not have the concept (*"what is a
            splash?"*), so it was re-run knowingly: a storm on open ground, watching where the
            drops meet it. *"No, could not see"* — **confirmed absent by a primed observer with
            a screenshot**, which upgrades the answer from *not noticed* to *looked for*.
            And the verdict on whether it matters: *"Nah like it's a cosmetic."*
            **So M7 closes on this question rather than around it**, and the four M7c-2
            findings go to the cosmetics pass below rather than back on an anonymous shelf.
            **The general lesson is worth more than the answer: a null result from an
            observer who lacks the concept is not the same null as one from an observer who
            has it, and the difference costs one question to find out.**
      - [x] **Answered 2026-09-02: it feels right, and x40 was a little fast.** *"In sealed ponds
            on the surface it works perfectly... a little too fast, um like you could half the
            fill speed, it would still work as well I reckon."* **So the project-level principle
            holds — *slower than realtime is accuracy, not approximation* survives contact with
            a person watching a pond** — and the only correction asked for is the scale, now
            **x20**. Note the judgement is on the spawn's shallow terraced ponds; a deep valley
            is the recorded revisit trigger.
      - [x] **Answered 2026-09-02: yes, tolerable.** *"The 4 m shoreline is ok, like it would be
            better to fill exactly but I honestly don't know how to do it and I ain't gonna task
            you with doing it just to satisfy a base cosmetic."* **So M7d-2's accepted cost is
            accepted by the person who has to look at it**, and refining shore cells stays the
            thing M8 deletes. **The same answer also carried the milestone's sharpest question
            out with it** — see "the minimum drainable hole" under Phase 3.

#### Order, and the case for a different one

- [ ] **a → b → c → d.** M7b needs the curves; M7d needs a surface to draw; M7c needs only
      `SkyColumn`.
- [ ] **M7c could go first** — it is the part with something on screen, and M6d-1 set the
      precedent that a later part goes first when it answers an earlier one's question. Not chosen
      because the wet term's *recent rainfall* wants the weather function to exist, and stubbing
      it writes the seam twice. **If the milestone starts to feel long with nothing visible,
      reordering c ahead of b is the intended relief valve** and costs a debug slider.

#### Deliberately not in M7

- [ ] **Tier B and Tier C.** Moving water, seepage, water in your excavation, pumps — M8. M7's job
      is to hand it a volume and a level.
- [ ] **Edits changing the watershed.** The seam is `SkyColumn` and it is already persisted.
- [ ] **Rivers.** Overflow between basins is arithmetic at M7; a visible stream is moving water,
      which is Tier B.
- [ ] Rain while the world is closed, and any offline progression.
- [ ] **Erosion.** The terrain is a pure function of the seed and non-negotiably so.
- [ ] **Audio.** The project has none, and rain is not the milestone to introduce a subsystem.
- [ ] Snow, temperature, biomes, seasons — every one is a second output of a function that
      currently returns an intensity.
- [ ] A second coarse watershed tier for lakes larger than the window. Trigger named above.

### M8 — Water gets in ← **planned 2026-09-02, split M8a–M8d, not started**

Planned in [docs/M8-PLAN.md](docs/M8-PLAN.md). **Read SCALE.md first**, and read M7’s two play
findings — they are inputs to this milestone rather than context for it.

**The structural point: Tier B is the first state in this project that is a *trajectory* rather
than a value.** Worldgen is a function of the seed, light is a fixpoint of the voxels, Tier A
water is a function of volume and terrain — pour in N litres and the level is solved however the
litres got there. Where moving water *is* depends on where it came from. **So its correctness
condition is that it terminates**: every active cell must eventually sleep and merge into a Tier A
body, and a CA that keeps cells awake forever is not slow, it is *wrong* — and it will read as a
performance problem until somebody notices it is a termination problem. **A cell oscillating by
one unit forever passes every volume-conservation assertion.**

Seven decisions are argued in the plan; the three that constrain the rest:

- [ ] **Tier B runs at voxel resolution, 0.25 m.** SCALE records this as open and a 64x swing; it
      is forced by the breach rule below, and **the cost is 16x rather than 64x because the active
      set is a waterline, which is an area** — ~14,400 cells for SCALE’s generous cave, ~28,800
      visits/s at 2 Hz, **about 5.5% of what the light budget already licenses**. *Derived from
      SCALE’s constants, not measured.* **Revisit trigger: the first sustained flood whose active
      set exceeds ~50,000 cells.** Re-derive SCALE’s sizing section after M8a — it asks for that
      by name.
- [ ] **A breach is detected at voxel resolution on the cell *face*, never by re-analysis** — the
      answer to the play’s question, and M5b’s cell-face rule with “water” for “light”. **Tier A
      never learns the hole exists**; it loses volume and re-solves its level, which is arithmetic
      `BasinWater` already does.
- [ ] **Tier C is integrated forward, not swept** — M7’s trick, because saturation is a slow scalar
      driven by analytic rainfall. **The honest cost: lateral drainage couples cells and has no
      per-cell closed form, so this is the part most likely to have been costed wrong**, and it is
      named for that reason exactly as M7a named its analysis.

- [ ] **The play has already asked for this, 2026-09-02** — a pond above a dug tunnel does
      not fall in, and a cut rim does not drain. **First user-facing consequence of "edits do
      not change the watershed at M7", reported unprompted**, so M8 has a play-confirmed
      motivation rather than a designed one. The seam is still `SkyColumn`.
- [ ] **Build the provocation seam first — a debug relay, and it is Matthew’s call, 2026-09-02:**
      *“The teleport-level tech will be endgame, but for the purposes of testing? Invaluable.”*
      **The hardest part of testing M8 is not the CA, it is getting water to move at all** — a
      flood needs a full basin, a breach in the right place, and the patience for both. M7 hit the
      same wall and answered it with seams (`--clock`, `--rainscale`, `--weather`, `--norain`,
      `--nowater`), every one of which paid for itself.
      - [ ] **It is also M8a’s measurement instrument**, since the number M8a exists to take is a
            9,000,000 L lake draining into a shaft — set up in seconds rather than in a session.
      - [ ] **A debug bind, not content: no material, no JSON, no ID.** The entangled miner’s rule,
            second outing — M5d kept `reachMetres` out of `ToolDefinition` because *once that field
            exists, every mod writes against it and the decision is gone for every tool*.
            Non-negotiable 2 is the same trap one level up: **the moment `minercraft:relay_pump`
            exists as a material the name is permanent, finished design or not.**
      - [ ] **The trap it carries is the milestone’s own oracle.** A debug source creates litres
            from nothing, which is exactly what volume-conservation-with-no-tolerance forbids — so
            it **will** fail the check, or worse, be quietly subtracted from it by somebody making
            the check pass. **Injected and removed litres are reported numbers**, and the oracle
            becomes `held == rained + injected − spilled − removed`. **Third outing for “print the
            denominator”**, and the first where the instrument itself moved it.
#### M8a-1 — the automaton and the active set ← **built 2026-09-02, suite 814 → 850**

`Overburden.Core/Fluid/` holds `FluidConstants`, `IFluidTerrain`, `FluidGrid`, `FluidRules` and
`FluidSimulation`. **Nothing feeds it — that is M8b.** Split M8a-1 / M8a-2 on the usual seam: this
part is the automaton and its oracles, M8a-2 is the measurement.

- [x] Tier B volume-conserving cellular automata over an active-cell set
- [x] Sleep/wake rules
- [ ] Settled cells merge into a Tier A body — **M8b**, and it is the half the play asked for
- [x] **The unit is the millilitre, and it corrects DESIGN.** A 0.25 m cell is **15.625 L — not a
      whole number** — so DESIGN's "amount 0-255" makes one unit 61.27 mL and every conversion
      across the seam rounds, which a no-tolerance conservation oracle cannot survive. Capacity is
      **15,625 mL, an exact integer division** of 1,000,000 mL/m³ by 4³ voxels; at three voxels per
      metre it would truncate. Costs one byte per cell. DESIGN is amended in place.
- [x] **The sideways divisor is fixed at five, and that is a correctness decision.** Dividing by
      *eligible + 1* — the obvious rule — makes a cell's move depend on its neighbours' amounts
      rather than on the pair, so **a neighbour gaining water can shrink the divisor and create a
      move with nothing awake to take it.** Caught by `VerifyQuiescent` on its first run, with
      four cells one millilitre out of level and every volume assertion green. The fixed divisor
      makes the rule local to a pair, which is what lets the wake rule stay at one neighbourhood
      per transfer. **Cost: levelling two cells takes ~16 ticks instead of 1; the frontier still
      advances one cell per tick, which is what a person sees.**
- [ ] **The play called the cost (2026-09-07): *"the amount of water that spreads into empty
      cells is a little low"*, 172 relay stabs on the fresh world.** Read off the log first: the
      tick held 2 Hz for nearly the whole session (80 ticks per 40 s at 9,000–16,000 active) and
      fell to ~1 Hz only in the last minute at 36,000 active, so this is the rule at its nominal
      rate, not starvation. What the rule does at a straight front: the edge cell gives a fifth
      of its surplus to the one dry neighbour ahead, so the skirt beyond a full cell is 3,125 →
      625 mL and stops at the litre floor two cells out. **Planned, not built — a counted
      divisor revisited**: *eligible + 1* was rejected above for a wake defect, not for its
      physics, and the fix was named then (wake the destination's neighbourhood too, ~2× the
      visits per move). It gives a straight front half the surplus instead of a fifth, levels a
      pair in one tick instead of sixteen, and is identical to today's rule in the one case
      where all four neighbours are dry. The proof holds (`d = k + 1`, `(k+1−2d)Σt² =
      −(k+1)Σt²`), the per-pair residue bound is unchanged (`d ≤ 5`), and `StepMillilitres`
      reads the maximum divisor. **Wants a bench arm before it is believed**: `--fluid`,
      `--spill` and `--lake` with the counted divisor against the fixed one — ticks to rest,
      total visits to rest, `VerifyQuiescent` on every arm, the lifecycle fuzz — because the
      claim is that fewer ticks to rest outweighs twice the visits per move, and that is a
      number. **Not the lever**: `SpreadDivisor` 5 → 4 empties the source against four dry
      neighbours and bounces it (a, 0, a/4 …), 5 → 3 clamps into a fan. The tick rate is the
      other honest lever and waits for the water thread, which is what holds 2 Hz past ~20,000
      active in Debug. `--settleafter=0` levels the mound under inflow today and does not move
      the front.
- [x] **Benched (2026-09-07, Release, machine control 1.02–1.04×): the counted rule is built as
      `SpreadRule.Counted`, `--spread` on the bench and `--spread=counted` in the game, the
      default stays fixed, and the prediction above was wrong in the way `--front` was built to
      show.** Suite 1,134 → 1,179; seven recipes, five caught (the dropped destination wake by
      both fuzzes and **not** by M8a's own 8×8 floor, kept as a regression), two identities. The
      fixed arm reproduces the M8b-1 table to the visit (39,809,482 and 111,222,865), so the
      visit's restructure moved no digit. What the numbers say:
      - **The wake M8a named was two cells too wide, and only the bench could see it.** "Wake
        the destination's neighbourhood" is six cells; the cells whose move reads the destination
        are the one above it (room only, which shrank) and its four sides (whose count can
        shrink), so the sides are the complete set. Woken as six, **the one-voxel bore drained at
        exactly half a cell a tick — 7.8 L/tick against 14.3, both shapes, to the litre — with
        nothing sideways in the shaft at all**: the two vertical wakes reordered the column.
        Trimmed to four, bore 1 is 15.1. Every figure below is the four-side wake; the six-wake
        arm is in the worklog beside it.
      - **A pool's front is diffusive, and a divisor moves diffusion as its square root.** On the
        flat floor (`--front`, R's 10,000 L) the counted rule reaches radius 15 / 26 / 44 at ticks
        20 / 100 / 500 against 13 / 22 / 38 — a fifth further, not twice — and rests in 6,705
        ticks against 7,825 with 7% fewer visits at 1.7× the CPU per tick, because the per-visit
        cost rose with the wakes. The pair arithmetic was right; the interior of a mound has two
        or three lower sides and the front is gated by the litre floor, so 1.5× the diffusivity
        is 1.22× the reach.
      - **The front the player sees is the solve's, and the counted rule nearly doubles it once
        the sheet is level.** `--front` with the solve at every boundary: fixed reaches radius 20
        at tick 20 over 1,090 wet cells, counted reaches **35 over 2,716** — a sheet of 4,145 mL
        across the floor against one of 9,934 — both resting in ~100 ticks. At a levelled edge the
        count is one and the edge cell gives half. The `--settleafter=0` claim above — *does not
        move the front* — was reasoning, and the measurement says the solve moves the front more
        than the divisor does; the play runs the settle after two quiet ticks, so what Matthew saw
        was nearer the fixed-plus-solve row than the fixed row.
      - **A spill with the hand-off is the clear win.** `--spill` 10,000 L with the hand-off rests
        in 65 ticks against 170 with 41% of the visits and 64% of the CPU; 50,000 L in 106 against
        314 with 32% of the visits and 45% of the CPU; with the settle as well, 15 against 18 and
        27 against 29. The automaton alone is 6,628 ticks against 9,111 and 61% of the visits.
      - **A flood drains faster and costs more per tick.** The 96 m mine: bore 1 15.1 L/tick
        against 14.3, bore 3 31.3 against 24.6, bore 9 106.0 against 92.1, at 1.6–1.7× the ms/tick
        and ~1.5× the settle's visits under the cap; the chamber's settle 1,471 ticks against 1,915
        with 29% fewer visits, and with the solve 67 against 44. The played lake at rest
        (`--lake`, share 8) costs 1.4× the solve's visits and 1.7× the automaton's. **In the Debug
        build the 2 Hz tick that held at 9,000–16,000 active would hold at roughly 6,000–10,000**;
        the water thread is the answer to that, not the divisor.
      - **Recommendation: the default stays fixed and Matthew plays `--spread=counted`** (pass it
        to `start.cmd`), because the thing the play asked for is a look and the row that answers
        it is the solve's. If it reads right the default flips and the water thread absorbs the
        per-tick cost. **Revisit trigger for the fixed rule: the first play that reads the
        counted front as right.**
      - **Played (2026-09-07, evening) — read off the log; the pool's edge is not yet judged.**
        Matthew: *"I think its good?"* The session ran `--spread=counted` (printed on the setup
        line) for 2,210 ticks on the played world: the identity `exact` on all 256 reports,
        `received == handed` exact, `paused` 0 throughout, and `displaced` 3,693 L — all of it at
        one chunk fill (`32768 voxel(s) placed`) into wet cells, which is the fill deleting what
        it fills, not the walk. **But `R` was never pressed** (`injected 0 L`), so the front the
        bench was built around was not looked at; what the play showed was the world's own
        drain (~350 L/tick for 1,400 ticks) and then chunk bites under lakes — 2.4 ML drawn in
        200 ticks, 187,021 active, the longest tick 526 frames — which is the Debug 2 Hz cliff
        at the counted rule's 1.7×, the water thread's case rather than the divisor's. **And the
        settle never ran: 0 passes in 2,210 ticks**, because `SheetSettle`'s quiet counter is
        global and the portal drain is inflow at every boundary — so what Matthew saw was the
        automaton's row (15 against 13), not the solve's (35 against 20), and the counted rule
        has still not been seen with the solve in play. The previous session (fixed rule, pours)
        ran 25 passes. Recorded as a provisional yes; the judgement the trigger names wants `R`
        on the fresh world, where the settle can fire.
      - [x] **Judged (2026-09-09): *"Well they both look like water?"* — the default stays
        fixed and the flag came out of `start.cmd`.** The A/B the bench asked for, made on a
        fresh scratch world with nothing awake: one `R` on flat grass under each rule, same
        spot. Matthew's count, the pour gone in about 8 s counted against about 9 s fixed —
        inside one report of each other. The logs: both pours wholly in the basin by tick 20,
        Tier B empty at quit, the settle 10 passes in each (the world was quiet, so it fired);
        at tick 9 the *fixed* arm had handed more (4,092 L against 2,455), because its pool
        stays thicker and whole litres on the ground go first, so the counted rule does not
        even make a spill vanish sooner. **The trigger read "the first play that reads the
        counted front as right"; it read as right, and so did fixed, at 1.7× less per tick.**
        The bench's radius 35 against 20 is a fact about the solve's front at tick 20 that a
        person watching a 10,000 L pool cannot see. Revisit trigger, narrowed: a spill on
        screen that reads as syrup, or the water thread making the per-tick cost free — then
        `--spread=counted` is one flag, already printed on the setup line.
- [x] **Termination has a proof, not a hope** — a sideways move strictly decreases the sum of
      squares (`(k+1−2d)Σt² ≤ −5Σt²` for `k ≤ 4`, `d = 5`), a vertical move strictly decreases
      Σ(amount × Y) which is bounded below because an unloaded voxel is solid, and the pair
      ordered lexicographically decreases on every move. **The proof also forbids the obvious
      tuning**: a minimum transfer of one millilitre breaks `t ≤ (a−b)/d` and oscillates forever —
      in the battery, and caught.
- [ ] **A cell under one litre does not spread sideways, and the cost is stated.** Without the
      floor, five litres reaches all 64 cells of an 8×8 floor at 78 mL each — a 1.25 mm film in a
      250 mm voxel, every cell of it held, drawn and re-woken. One litre because that is the
      smallest amount the Tier A seam can express. **Cost: a shallow spill settles in blotches
      rather than a level sheet. Revisit trigger: M8d's transparent pass making that visible**, or
      a play-test calling a puddle stuck.
- [ ] **The resting surface is level to within four millilitres *per adjacent pair*, and that
      accumulates along a path** — measured at 8 mL low across a three-cell join, with 12 mL left
      standing a level above it. 6.4 mm across a hundred-cell tunnel. **Not** a global bound, and
      the two tests that asserted one both failed.
- [ ] **No pressure, therefore no siphons.** Water falls and spreads, never rises; a U-bend does
      not fill its far arm. Filling a mine still works, because water arriving from above meets
      each side passage at that passage's own height. **Named because it is the first thing that
      will look like a bug and is not one**, and it is a test rather than a comment.
- [ ] **Panels are dense and a flooded tunnel network is not.** One 64 KiB `ushort` panel per
      chunk holding water, released when the last drop leaves; a chunk holding one cell costs the
      same as one holding 32,768. `PanelCount` is printed beside the held-cell count so the
      sparsity is visible. **Revisit trigger: panels averaging under ~1% full across a sustained
      flood**, where a run-length or octree panel starts to pay for itself.
- [ ] **`Wake(to)` is an identity today and is kept for the relay.** Every destination is a face
      neighbour of the source, so removing it breaks nothing and cannot. **The debug relay is a
      transfer between cells that are not adjacent**, and the moment it exists this line is the
      only thing waking the far end. Named rather than deleted.
- [ ] **`VoxelFluidTerrain` tests for air or an existing fluid, deliberately not for "not
      opaque".** Glass is transparent and solid. **Revisit trigger: the first material that is
      neither solid nor fluid** — a ladder, a rope, a grass tuft — which wants a real `IsSolid` on
      `MaterialDefinition` rather than a second special case. M8c adds `permeability` to the same
      file, which is the natural moment.

#### M8a-2 — the measurement ← **done 2026-09-02, `--fluid` on the bench**

- [x] **Peak active cells, cell visits per second and time to quiescence for a 9,000,000 L lake
      draining into a shaft**, printed beside the light budget's ~520,000 visits/s.
- [x] **Re-derive SCALE.md's sizing section** — done in place; the 64x caveat is closed.
- [x] **The play's question is answered with a number, and the answer is no.** *(Figures in this
      block are the M8a-2 table's, which the committed code does not reproduce — see M8b-1 for the
      baseline: 29 L/s, 51 h, 6.0%, peak 4,096–20,737. The conclusions stand.)* *"Unless I make a
      hole 4x4 it isn't going to drain into eventually?"* — **every** hole drains, down to a single
      0.25 m voxel, at **30 litres a second**. The 4 m figure was Tier A's analysis lattice and
      Tier B never sees it. **The drain rate is close to linear in the bore's width and nowhere
      near its area** — 9x the width gives 9.0x the rate while offering 81x the cross-section, so
      past the first voxel the constraint is the 0.75 m tunnels it empties into. Third outing for
      *the characteristic feature of this game is three voxels wide*. **A `hand_pick` bore empties
      a 9,000,000 L lake in 38 hours of world time**, which is the first number this project has
      for how long losing a mine takes.
- [x] **Decision 1 is confirmed by measurement: 0.25 m cells are affordable.** Peak active
      2,706–17,357 against M8-PLAN's derived ~14,400; **26,701 visits/s at 2 Hz against the
      ~520,000 the light budget licenses — 5.1% at the widest breach.** The ~50,000-cell revisit
      trigger is not reached.
- [x] **The tick wants a time slice, the way the light step has one.** 0.44–2.71 ms mean per tick
      is half a percent of one core at 2 Hz, but **2.7 ms is a sixth of a frame arriving at once**.
      Named 2026-09-02, **built 2026-09-04 as the tick budget slice** — see M8b-1's item below.
- [ ] **THE FINDING THAT DECIDES M8b: it does not settle, and the active set does not decay.**
      Every 96 m arm hit a 5,000-tick budget — 41.7 min of world time — with **9,000 to 21,000
      cells still awake, more than were awake during the flood**, because the water keeps
      spreading after the inflow stops. It *does* terminate: a 16 m mine reached full rest in
      **11,071 ticks, 92 min of world time**, with `VerifyQuiescent` confirming it. The two points
      fit **O(n²) in the length of a flooded corridor** — 96 m extrapolates to ~55 hours.
      - [ ] **The decay-curve instrument returned the opposite of what it was built to show.** It
            assumed "the flood stops costing" happens long before "every millilitre is level". The
            16 m arm stayed at ~1,700 active for **10,992 of its 11,071 ticks** and collapsed in
            the last 79. **A flood keeps its whole wetted area awake until it is completely level;
            there is no cheap early exit in the curve.**
      - [ ] **So DESIGN's *sleeping is load-bearing, not an optimisation* is measured rather than
            asserted, and M8b's Tier B → Tier A handoff is the milestone rather than the CA.**
      - [ ] **The honest other half: the cost of not having it is bounded and small.** 21,000 cells
            at 2 Hz is 8% of the light budget. **A design failure rather than a performance
            emergency** — which is worse than what M8-PLAN warned about, since it looks like
            neither.
      - [ ] **Named fallback, deliberately not built: a coarser rest tolerance.** It would end the
            settle far sooner at the cost of a *visibly* tilted surface, since the residue
            accumulates along a path. **Do not build it before M8b measures how much the handoff
            removes.**
- [ ] **Two instrument defects, and the first produced a wrong number.** The fixture answered
      `CanHold` by calling `IsCaveAt`, so six neighbour queries a visit each evaluated eight
      octaves of noise: **3.86 µs a visit against the light propagator's 0.457.** Baking the
      terrain into a bitset took it to 0.20–0.32 µs. *Check whether observing changed what you
      observed* — the first reading was 12–19x the truth. And **conservation and quiescence shared
      a column**, so an arm whose settle hit its budget printed `*** NO ***` under `conserved`.
- [ ] **`FluidGrid`'s panel trigger fired on its own first measurement, and it was the wrong
      trigger.** *"Revisit when panels average under ~1% full"* — measured at **0.37%–0.80% on
      every arm**, because a tunnel grid is a sparse skeleton cut through solid rock. What it cost
      was **4.2 MiB**, a twentieth of one drawn chunk's mesh budget at r12. **Restated as an
      absolute: revisit when `PanelBytes` passes ~64 MiB in a played session.** A ratio a sparse
      world will always fail is not a trigger, it is a complaint.
- [ ] Tier C coarse groundwater saturation field, fed by rain, drains downhill — **superseded by
      M8c's decision 1 (2026-09-11): the table is Tier A's level, derived and stored nowhere.**
      - [x] **Read `permeability` from the material JSON, per material.** Done 2026-09-11 at
            M8c-1: `MaterialTable.Permeability`, refused outside [0, 1] at load, NaN sealed at use,
            with a shipped-content test that something leaks and something is sealed — see
            [docs/MATERIALS-NOTES.md](docs/MATERIALS-NOTES.md).
- [x] Seepage into excavations driven by saturation × material permeability — **built 2026-09-11
      (M8c-1 the index, M8c-2 the draw), unplayed.**
- [ ] Transparent render pass, back-to-front sorting
      - [x] **Decided 2026-09-03: a grid per fluid kind. Tier B was single-fluid, and a gas is a second fluid kind
            with gravity's sign flipped.** Heavy gas (CO₂, blackdamp) is water's rule verbatim;
            light gas is the inverse, dispersing at open sky — *"the reverse of rain"*, Matthew,
            2026-09-03. **Recommended the same day: a grid per fluid kind, keyed by material ID,
            ordered by the JSON `density` field** — not a kind byte per cell, which cannot put gas
            in the room above half a cell of water. Options and reasoning in
            [docs/MATERIALS-NOTES.md](docs/MATERIALS-NOTES.md). **Settled 2026-09-03 — Matthew:
            *"Ok lets continue"* on the recommendation.**
            - [ ] The seam, as M8b's first commit: `FluidSimulation` reads gravity from one `Down`
                  field rather than six literal offsets, and *room* is a method that today returns
                  `FreeAt`. No grid, constructor, caller or test changes; re-run `--fluid` after.
- [x] **Settled 2026-09-02: option 3.** *"Keep M8 clean, tune seepage low."* M8 makes the mine
      floodable and nothing fights back until M10, since pumps are machines — so the seepage rate
      ships low enough that flooding is a nuisance rather than a defeat, and M10 turns it up once
      a counter exists. **The deciding cost was not the pump but what it drags with it**: a machine
      is the first *entity* this world holds, so bringing one forward would have pulled an entity
      table and **header v4** into the milestone whose oracle is exact volume conservation, and
      **two structural firsts in one milestone is how a defect hides behind a feature.** It also
      keeps M8d’s play honest — *"is losing to the water tolerable?"* is the real question for a
      milestone with no counter in it, and judging a pump there would have judged M10 early.
      **The rate is the lever, same shape as `rainfallScale`**, which the play has already moved
      once.
- [ ] **Swimming, drowning and buoyancy are not in M8, and are named because they are the first
      thing a play-test will try.** The player does not interact with water as a body at M8; that
      wants its own decision.

#### M8b — the seam ← **split four ways 2026-09-03, before any code; see M8-PLAN's M8b section**

Three things the planning pass changed, each of which the plan had stated one level above where the
answer sat: **the cell-face rule is trivial at the voxel** (a face is one pair); **a flooded mine
has no Tier A body**, so resting mine water is *stored*, in a `fluid` table beside `columns`, and
decision 5 becomes *settle, then store what settled*; and **the settle is a solve rather than a
wait** — flood-fill the region, sort by Y, pour from the bottom — which is what answers M8a's O(n²)
finding without the coarser-tolerance fallback.

- [x] **M8b-1 — the seam edits, the chamber fixture, the tick in the game loop, and the relay.
      Built 2026-09-03, suite 892 → 895.**
      - [x] `FluidSimulation`: `_fallSign` as one field, `RoomAt` as a method; two break recipes fail
            six tests each. **`--fluid` re-run: no digit moves against the committed code — and the
            committed code does not reproduce the M8a-2 table.** The control arm (edits stashed)
            gives the identical visits; the table was a shorter window and a pre-commit automaton.
            **Baseline is now the committed code's**: peak 4,096–20,737, 29 L/s at one voxel, 51 h
            for a `hand_pick` bore, 6.0% of the light budget. *"9× width → 9.0× rate"* is
            window-dependent (6.3× over 2,000 ticks, 10.5× over 1,000): **quote the window**.
      - [x] `--fluidshape=chamber --fluidchambers=<n>`, a bore of 32+ opening the whole roof. **An
            8 m floor settles (1,915 ticks, rest real) where a 96 m corridor does not; a roof falling
            in is a one-tick spike of 43,374 cells for one chunk and 302,182 for 2×2×2 — 73.5 ms in
            Release, ~0.7 s in Debug — six times the 50,000-cell trigger, in one tick.**
      - [x] **The tick budget, with its number attached: `Step` has to be resumable across frames.
            Built 2026-09-04, suite 936 → 953.** `FluidSimulation.Resume(maxVisits)` is the cursor
            over the batch, `Step(deadline)` resumes in granules of 64 visits against an injected
            clock, checked after the granule and never before; `Main` begins at 2 Hz and resumes
            every frame under `--fluidbudget=<ms>`, 2 ms by default (an estimate; the sweep is the
            flag). **Sliced equals whole to the digit** — cell for cell over 400 ticks at four slice
            sizes in the suite, and every bench figure identical between the arms. Release: the
            302,182-cell roof-open tick is 38 slices at 2 ms instead of one 80 ms frame, bore 9
            needs two slices for 436 of 500 ticks, and slicing costs nothing per tick. Debug: a
            frame pays mean 2.00 / max 3.05 ms instead of 14–23, the longest ordinary tick is 17
            frames, and the 2 Hz rate holds. Fourteen break-recipes, all caught after three fixture
            repairs (a hang in Core's deadline loop under a frozen fake clock; a Settle recipe
            unreachable by a falling fixture; a counter both arms of the comparison shared). The
            worklog entry of that date has the tables.
      - [ ] **`Begin()`'s `_queued.Clear()` is the overshoot at the largest active set — a lever,
            not built.** The budget's bound is *budget plus one granule*, and the first granule of
            a tick clears a `HashSet` proportional to its capacity: 0.9 ms over on the 302,182-cell
            tick, 0.1 ms on the mine. Amortise it (swap two sets, or clear at `Complete`) only if a
            play reports the roof-open frame; today it is one millisecond once per tick at a peak
            nothing but a bitten chunk under a lake reaches.
      - [ ] **Play question for the slice: does a roof-open flood read as water arriving or as the
            game stalling?** In Debug at 2 ms a frame the 2 × 2 × 2 tick is ~400 frames — six or
            seven seconds of a chunk filling from under a lake. DESIGN calls that accuracy; a person
            holding the thing decides whether it is. `--fluidbudget=` is the lever, and M8b-2's
            breach is what makes the case reachable in play.
      - [x] `ChunkPipeline.onVoxelChanged`, told after the write and silent on a no-op; `Main` ticks
            at 2 Hz over `VoxelFluidTerrain`; `Disturb` wakes only cells holding water and returns
            at once on an empty grid. Three tests.
      - [x] The relay: `R` puts 10,000 L down at the crosshair in a ball sized to hold it, Alt+R takes
            back 2 m of it, `--injectlitres=<n>` puts a sized ball on the ground under the player when
            the measured window opens. **Sized, because a fixed 1 m ball is a hose**: the automaton
            falls one cell a tick and spreads diffusively, so a source drains at its boundary's rate
            — 15,871 of 50,000 L delivered mid-air, 11,837 on the ground. No material, no JSON, no ID.
      - [x] The profile: `fluid` / `fluidtick` / `fluid ms` / `fluidsurf` lines and an `M8b:` line,
            every term of the identity, the undelivered remainder, visits/s against the light budget.
            **Streaming unchanged to the digit**: 1521 / 165 / 1926 at 404 with 50,000 L spreading;
            2601 / 325 / 3446 at 411 for the r6 default. **The r6 control needs `--nosave` and
            `--quit-after 2400`**: the bare run opens the played world and stands in its void.
      - [x] `FluidSurface`: one quad per top cell, rebuilt on Tier A's timer when the water moved.
            1,402 quads at 1.82 ms for a 50,000 L spill; screenshot against a `--nowater` arm.
- [x] **The derive queue drains nearest-first — built 2026-09-03, suite 910 → 912.** `near lit`
      (every chunk within 2 of the viewer derived) on the played world at a standing start:
      **5.14 s → 0.62 s windowed, 3.27 s → 0.56 s headless**; the seed world 2.24 s → 1.04 s;
      `light settled` unchanged (6.2–6.3 s) because the order moves nothing's total. `--derivefifo`
      restores the FIFO arm. Four break-recipes, four caught. `AllDerivedWithin` is the instrument
      and `first draw` was never going to be — inside a void nothing near the player draws.
- [x] **`start.cmd` carries `-- --lightbudget=16`** (2026-09-03, Matthew: *"remember we need the
      lighting switch"*). The play launcher's setting, not the pipeline's default.
- [ ] **Decided 2026-09-03: the light derive stays on the sim thread** — Matthew: *"skip
      multithreading."* Proposed after he outran the derive at sprint speed (r6h/4v is 221 chunks a
      step, ~660 derives a second at 16 ms in Debug, so walking keeps up and 30 m/s does not). The
      shape was sound — a derive job like generate and mesh, voxels already copy-on-write, the sky
      column snapshotted, only the apply and the propagation left on the sim thread — and it is
      not built. The levers on the cliff stay as recorded: `PaddedChunk.CopyFrom` (60–75% of a
      derive), a Release build, and the box size. Cache slice 2 keeps its point as a result.
- [ ] **Should 16 ms be the Debug default rather than the launcher's flag?** Open. For: the build
      people play is Debug, and a budget is a ceiling, not a reservation — once lit it costs
      nothing. Against: every headless figure since M5c-1 was taken at 4 ms, and a default that
      differs by build configuration is a seam nobody sees. Decide after a play at 16 with the
      nearest-first queue, which may make 4 ms feel fine again.
- [x] **The played world lights its own centre last, and at today's machine speed nothing draws
      for tens of seconds.** Found 2026-09-03 from Matthew's "the ground is gone" screenshot; the
      worklog entry of that date has the table. `_lightBuilds` is FIFO, stored chunks arrive one at
      a time on the serial lane after the generated ring, a mined-out region is stored chunks all
      the way round the player, and a Debug derive at the machine's 2.3× (3.9 ms) exceeds the 4 ms
      budget so it is one derive a frame. **Recommended fix: order the derive queue by distance to
      the viewer**, on the scheduler's band and timer. Then measure `first draw` on `probe-load`
      against the 14.09 s recorded, windowed as well as headless, because the windowed frame is
      the one that gives a single derive a frame. `--lightbudget=16` is the workaround until then.
      `PaddedChunk.CopyFrom` stays the named lever on the cost itself. Also recorded there as a
      question: the window holding 640.6% of its leaf capacity with 0 L spilled.
- [ ] **Give `--light` a machine-control line.** `--watershed` prints one; `--light` does not, and
      on 2026-09-03 a 2.3× reading was Matthew's own game running beside the bench, which a control
      line scales with and cannot separate. Print the worldgen control *and* say whether another
      Godot process was found. Also check `tasklist` before any timing, as a habit.
- [x] **The retained-chunk cache, slice 1 — voxels and sky columns. Built 2026-09-03**, suite
      895 → 910. See [docs/CHUNK-CACHE-PLAN.md](docs/CHUNK-CACHE-PLAN.md), "Slice 1 — built".
      `--chunkcache=<MiB>` (64, 0 off) and `--turnaround=<seconds>`. A turnaround's loads and
      generates go to zero for the cached chunks and its derives do not move; `viewer ms` mean
      6.6 → 0.9 ms because a hit skips the column seed. `Forget`'s stale comment is corrected.
- [x] **The retained-chunk cache, slice 2 — derived light. Built 2026-09-04**, suite 926 → 936.
      See [docs/CHUNK-CACHE-PLAN.md](docs/CHUNK-CACHE-PLAN.md), "Slice 2 — built". A face edit
      drops a cached neighbour's grid; a moved, restored or replaced column is caught at revival by
      `SkyColumn.Revision`; values are cleared on entry and reseeded in the light step. Derives
      5,157 → 3,572 at 6 m/s, 8,454 → 5,611 at 20 m/s, and the 20 m/s turnaround arm draws where
      the control never does. Still nothing for a first fill.
- [x] **Three light repairs the slice 2 walk found, none of them the cache's (2026-09-04).** A
      departed grid's light is retracted from its neighbours (`LightPropagator.RetractFrom`, the
      inverse of `SpreadInto`) — the whole loaded set was never a fixpoint before; the un-light
      walk reseeds past a cell that held 1 (a hole since M5b); and a face conducts only when both
      cells say so (a stale one-sided bit leaked skylight into rock while a neighbour's gain
      re-derive was pending). Each has a test in the suite; the cap-0 arm of the walk is the
      control that attributed them.
- [ ] **`viewer ms` is column seeding, not the difference — decide whether to chase it.** Found by
      the cache: a hit takes the sweep from 6.6 ms mean to 0.9, so `SkyColumn.Seed`'s 1,024
      surface evaluations per column plus a record per chunk is most of what M6d-2 measured and
      chose not to chase. The p95 is unchanged because the first fill has nothing to hit. The
      lever would be seeding columns on a worker the way the restore already runs, or lazily.
- [ ] **M8b-2 — A → B, the breach. Planned 2026-09-05, 2a built the same day; see M8-PLAN's
      "M8b-2 — the planning pass" and "M8b-2a — built".** Split three ways, in order. **Both
      decisions taken as recommended (2026-09-05, *"Lets continue"*)**: a hole is what was dug —
      portals are found through edits only, with the revisit trigger the plan names — and Tier A's
      height convention is fixed.
      - [x] **M8b-2a — `BasinWater.Remove` and the height convention. Built 2026-09-05, suite 953 →
            963.** `LakeTop` is the one walk `LevelAt` and `Remove` share; `Remove` takes from the
            top slab down the path, never a sibling, `Refresh` once, returns what it took.
            **The oracle was stated backwards**: `Remove(x)` then `Add(what it took)` is always the
            identity; `Add(x)` then `Remove(x)` only inside one lake — water that crossed a ridge is
            the neighbour's. **And `LevelAt` reported one lake at two heights since M7a-2**: a
            composite closed at its children's height has a slab that can never hold anything, and
            the old walk stopped at it; three pits on a flat shelf is the fixture. `HeightField`
            samples `SurfaceHeightAt + 1`, pinned to `SkyColumn.Seed`'s plane by a test; every
            played pond's surface is one voxel higher. Thirteen break-recipes, thirteen caught.
      - [x] **M8b-2b — the portal index. Built 2026-09-05, suite 963 → 974.** A portal is a pair
            `(Inside, Outside)` filed under the *outside's* sample, because that is where the lake
            is; a scan takes pairs touching the chunk from either side (a pit under a chunk
            boundary); an edit re-evaluates ten pairs with the seed surfaces read once.
            `SkyColumn.SeedSurfaceY` measured as the difference between **1.2 ms and 10.3 ms a
            bite** in Release (`--portal`). The natural-cave fixture had no caves at the shipped
            threshold and was repaired to assert 2,005 columns with one under the margin. Fourteen
            break-recipes, fourteen caught. **Lever, not built: a chunk wholly below its surface
            band skips the hook.** The dug-chunks-only rule is 2c's call-site filter.
      - [x] **M8b-2c — the draw, the wiring, the profile. Built 2026-09-05, suite 974 → 989.**
            `PortalDrain` at the tick boundary, `PortalTracker` owning decision 1 (plus the rule the
            plan missed: an unedited chunk arriving beside a dug one re-scans the dug one, and a
            chunk's first edit scans it), `StreamerSeedHeights`, `onVoxelsReady` on all three arrival
            paths and the forwarded forget, `--fillbasin=` (the Tier A relay the plan did not have)
            and `--breach=` at the basin's pit. First run: 200,000 L poured, 7 portals, 1,117 L drawn,
            both identities exact, Tier A drawn == Tier B drawn, the r6 control unchanged. **Found:
            the water line's `held` double-counted in-window basins since M7d-1 (fixed), and a
            dead-end one-voxel shaft takes ~1 L a tick after the first (the automaton's throughput,
            recorded for M8d).**
      - [x] **M8b-2c play — Matthew's, 2026-09-05: *"I am loving this water … all in all i really
            like it."*** He opened the played world and watched its lakes drain into the mine — the
            flood on open, seen and liked, which is the verdict for the seam. The pond's surface
            hanging over a hole was on screen in his second shot and not remarked on: silence
            rather than evidence, so M8d still owns the look. Not yet played: a tunnel driven into
            a lake bank on purpose, and the relay's water soaking into a hillside (the hand-off's
            own decision).
      - [ ] **A hole in a lake's bank is a treadmill (2026-09-09, read off the scratch world's
            log).** A chunk bitten at a lake's edge below the waterline files a portal; the lake
            drains into the hole, the hole fills (held sat at ~670 kL, a chunk and a half of
            cells), the overflow runs out over the hole's rim onto ground whose 4 m sample is
            wet — under the lake, by Tier A's model — and the hand-off gives it straight back
            to the basin it came from. `drawn` and `handed` then climb together forever:
            **10.5 ML drawn and 10.9 ML handed in 2,000 ticks, ~7 kL a tick each way, with
            8,000–10,000 cells awake the whole time** and `held` flat. Every identity exact; the
            seam doing what each half was told to. It is the "two surfaces for one body of water"
            case with a number, and it is why no world with a breach ever goes quiet (the item
            above). Neither tier can end it alone: Tier A does not know the hole's level and Tier B
            does not know the lake's. Two shapes, neither designed: the drain stops when the Tier B
            column under the mouth stands as high as the lake (the mouth already passes down only
            the room below it, so this is the same rule read against Tier A's level rather than
            the cell's), or the hand-off refuses a cell whose sample is under a lake whose level
            is above the cell (the water is already the lake's — leave it standing). M8c's, with
            the seepage design, or M8d's with the look; not before. **Built at M8d-3 (2026-09-11)
            as the second shape, one voxel stricter than written — submerged, not wet — and the
            first shape falls out of it: the drain stops when the column under the mouth is as high
            as the lake. The play check is above under M8d-3.**
      - [ ] **How to measure a spill in play — asked 2026-09-05: "IDK how much water is seeping
            into the hillside".** `handed` on the `M8b:` line and `received` on the `M7d:` line,
            every 5 s in the console behind the game and in `user://logs/godot.log`; press `R`, read
            `handed` before and after. **Nothing on screen shows it** — a `handed` / `received`
            readout on the HUD is the missing instrument for this decision, and is offered rather
            than built. Measure it on a fresh world (`start.cmd --nosave`): on the played world a
            tick is 1,267 frames, so a spill there moves a cell every twenty seconds.
      - [x] **Stated gap until M8b-4: a quit loses the pit's water. Closed by M8b-4b, 2026-09-07:**
            the pit's water is stored per chunk beside the lake's ledger, awake cells listed, and
            comes back to the litre and the cell. **And
            until M8b-3 a chamber that fills under a lake holds its water for ever** — no B → A
            handoff exists yet (closed by M8b-3a).
      - [ ] **A dead-end shaft's throughput is ~1 L a tick, and the plan's 30 L/s per portal assumes
            the cell under the mouth empties.** M8a's 29 L/s was a bore into a mine with tunnels.
            **Measured against the fall rule at M8d-3: not the descent's** — `--fluid` at bores
            1 / 3 / 9 reads 15.1 / 36.6 / 157.9 L a tick at one cell a visit and 15.2 / 37.8 / 159.1
            at sixteen, because a wet column is a column, not a fall. The lever is the mouth's
            offer (a bottom-up pass down the column, or the drain reading the column's room), and
            it is still unbuilt.
      - [x] **Matthew's fall rule (2026-09-07): let a cell fall 2, 4, 8, 16 cells a tick when the
            cells below are vacant, so falling water is not 0.5 m/s. Built at M8d-3, 2026-09-11,
            as the stateless version, 16 the default; see M8-PLAN's "M8d-3 — built".** Priced on
            paper first: the visit already reads the cell below, so probing up to sixteen is at most 16
            reads a *falling* cell, and falling cells are a column where the active set is an
            area — a 1,000-cell fall front is 32,000 reads a second at 2 Hz, ~6 ms a second in
            Release, a rounding error on the 2 ms-a-frame budget. The termination proof survives
            (a longer fall lowers Σ(amount × Y) by more); the cells passed through are empty and
            need no wake, the landing's neighbours do; the hand-off reads rest and is untouched.
            **Two versions**: "fall as far as is vacant, up to N" needs no state and is terminal
            velocity from the first tick (4 m a tick at N = 16, 8 m/s); true acceleration —
            one, then two, then four — needs a velocity byte a cell, which doubles the panel
            (78 MiB → 156 MiB on the played lake, M8b-4c's number). Recommend the stateless one,
            swept by a flag, judged in play; it belongs with M8d's waterfall question.
- [ ] **M8b-3 — B → A, the handoff and the settle. Planned 2026-09-05 and 3a built in the pass; see
      M8-PLAN's "M8b-3 — the planning pass".** The measurement came first, as the plan said, and
      the hand-off was built as its instrument.
      - [x] **M8b-3a — the hand-off. Built 2026-09-05, suite 989 → 1,006.** A cell the automaton
            found at rest on the analytic ground (`SurfaceHeightAt + 1`) hands its whole contents to
            the basin under its sample — `BasinRainfall`'s delivery with a different source — with
            the sub-litre remainder carried per pit and delivered when it makes a litre. **Measured
            (`--spill`, Release): the hand-off removes 99.3–99.5% of a surface spill's visits**;
            10,000 L rests in 170 ticks instead of 9,111, 50,000 L in 314 instead of never, Tier B
            empty in both. **M8a's coarser-tolerance fallback is dead for the surface.** An outlet
            cell spills; a tank on natural ground leaks as rain would (pinned; revisit trigger: a
            play reading it as wrong); a tank on a placed floor holds. Eighteen break-recipes, one
            hole — moving cells listed as rested passed every end-state test, because what it moved
            was *when*. `--fluid` unchanged to the visit; the r6 control unchanged to the digit.
      - [ ] **Decision for Matthew: does a spill soaking into the ground read right?** With the
            hand-off the relay's water runs down a hillside, pools where it rests, and each pooled
            cell drains into the basin the tick it stops — vanishing into grass and reappearing as
            the lake at the pit. Recommendation: ship it and let the play judge; the lever if it
            reads wrong is a delay (hand off after *n* rested boundaries), not a redesign.
            **Deferred by Matthew, 2026-09-05: *"Give me some time with the seepage metric, can we
            revisit the question when we have something for it to seep into."*** The hand-off ships
            as recommended and the question is parked until M8c gives the ground `permeability`,
            so that "soaking in" is a rate through rock rather than a hand-off at the plane. The HUD
            readout waits with it. Revisit trigger: M8c's planning pass, or a play that calls the
            vanishing a leak before then. **3b's gate is now the settle interval and his word.**
      - [x] **M8b-3b — the settle. Built 2026-09-05, suite 1,006 → 1,032; see M8-PLAN's "M8b-3b —
            built".** `FluidSimulation.LevelSheets()` levels every sheet the active set touches —
            wet, holdable, supported cells at one Y, sideways-connected — to within a millilitre,
            or packs a sheet whose share is under a litre into as few cells as hold it at 999 mL,
            bottom-up, through `Transfer` only, waking the front and the water above a drawn-down
            cell and listing the rest as resting; `SheetSettle` fires it after *K* quiet ticks
            (`--settleafter=`, 2 by default, 0 under inflow). **The chamber's 1,915-tick settle is
            38 ticks, the 16 m mine's 11,130 is 136, the 96 m mine's *never* is 258** — every rest
            real, every litre exact, visits down 50–200×. 23 recipes applied, 22 caught, one identity.
            - [x] **"A handful of ticks" is hundreds, and a pass per boundary is O(sheet) per tick
                  while a front creeps.** The solve levels; the front and the falls take the ticks.
                  At bore 9 on the 96 m mine each new front cell re-levels a 28,000-cell sheet —
                  44.7 M cell writes over 4,241 passes, 17 of the arm's 18 s, 1.8× cheaper than
                  waiting against hundreds of times elsewhere. **Lever, not built**: re-level only
                  when the unsettled pairs are more than a fraction of the sheet, or top up the
                  front from the interior. Revisit trigger: a `settle` line whose `ms in all` is a
                  noticeable share of a session. **Fired on the first play, 2026-09-05**: the
                  played world's mine became one 162,572-cell lake and was re-levelled at every
                  boundary for its creeping edge — 120,000–133,000 cells written a pass, 41.6 M
                  in ten minutes, the longest 275.6 ms in Debug, twice a second, in one frame.
                  **Next slice**: re-level a sheet only when more than one cell in *n* of it is
                  unsettled (small sheets always), `--settleshare=` the sweep; the automaton
                  levels a creeping edge locally within five cells and wakes nothing behind it.
                  **Built the same day as the share slice — see M8-PLAN's "M8b-3b — the share
                  slice".** `--settleshare=<n>`, 8 by default and an estimate, 0 the control; the
                  share is decided without flooding the sheet (the wake rule puts every unsettled
                  cell beside an awake one, so a flood from every seed at once knows the count by
                  its second layer and stops when the count reaches *n* times it), and a step
                  between two levelled puddles — narrow, not local — is levelled whatever the
                  share. **On the play's shape (`--lake`, a 147,456-cell sheet draining at one
                  edge) a pass is 20 ms → 0.08 ms Release and visits 144,194 → 2,911; on the 96 m
                  bore-9 mine the CPU halves and the ticks to rest double, the doubled half under
                  64 awake cells.** Suite 1,032 → 1,040.
                  - [ ] **Play: does a poured mound on the lake, or two puddles meeting, sit
                        unlevelled for longer than reads right?** The merge is the step detector's
                        case (32 mL, an estimate), the mound is the share's; `--settleshare=` is
                        the sweep and 0 is how it looked before. Read `left under share` and
                        `levelled for a step` on the `M8b:` line.
            - [ ] **The pass is atomic, 1–3 µs a cell in Release, and the played world's floor is a
                  ~1.3 million-cell sheet.** Seconds a pass in Release, tens in Debug, in one frame,
                  once the flood stops and *K* ticks pass. Not met yet — the play drained the lakes
                  and the flood on open is 91 kL in 29 ticks now. **The slice is the lever**: a
                  cursor over sheets like the tick's over its batch. Revisit trigger: the first
                  play that hitches when a flood stops. **Met the same evening**: 275.6 ms over
                  162,572 cells in Debug (1.7 µs a cell in the game, under the bench's figure),
                  once per merge of the lake's sheets; a quarter-second hitch each time, and the
                  per-boundary repeat above is the larger cost. **Re-measured with the share
                  slice's `--lake` arm the same evening: 0.14 µs a cell in Release** — the 1–3 µs
                  carried the bench fixture's lazy column bake inside the timed pass — so the
                  played floor is ~23 ms a pass Release, and the slice's trigger stands for the
                  Debug build people play.
            - [ ] **Play: does a tunnel floor levelling itself in one tick behind a creeping front
                  read right, and does *K* = 0's flat stream?** `--settleafter=` is the sweep; the
                  soak's look is deferred to M8c with the hand-off's decision.
            - [x] **✅ Built 2026-09-09 — the quiet counter is per chunk now.** `Add` and a waking
                  `Disturb` stamp their chunk with the tick; the sheet flood takes the max over the
                  chunks a sheet touches; a sheet is levelled only when its own chunks have been
                  quiet for K ticks, and `SheetSettle`'s global gate is gone. **Measured against the
                  stashed control on one invocation: 0 passes → 79, 0 sheets levelled → 818, handed
                  50,963 L → 54,912, active at the end 6,436 → 5,721**, for 439 ms of solve in a
                  40 s window. The flag's meaning is unchanged (`age > K`, not `>= K` — the world
                  counter zeroed the boundary the inflow landed on, and 80 s-at-2 against 12 s-at-0
                  hangs on that). **What it does not fix: the sheet that contains the inflow**, which
                  is the share rule's job and the honest limit of chunk resolution. 12 recipes;
                  the one that could not be caught — `Union` dropping the fed half — was answered by
                  moving the question to a walk of the sheet's cells at judgement, so there is no
                  merge to get wrong.
            - [ ] ~~**The quiet counter is global, and a lake draining into the mine is inflow~~
                  forever (2026-09-07, read off the play log).** `SheetSettle` resets its count
                  whenever the automaton's added millilitres or disturbed cells moved, over the
                  whole world; the portal drain adds at every boundary while any lake stands over
                  a breach, so on the played world the solve ran **0 passes in 2,210 ticks** at
                  `--settleafter=2` — the mine's flood is never levelled, and every finding that
                  says "the front the player sees is the solve's" is about a world without a
                  breach. The previous session, whose inflow was pours that stop, ran 25. Two
                  levers, neither built: count quiet per sheet or per chunk (a sheet the drain
                  does not feed goes quiet on its own — the natural fit once the water thread's
                  near/far order exists), or a cadence under inflow (a pass every *N* boundaries
                  while inflow persists, which `--settleafter=0` does at every one). The bench
                  arm is `--fluid` with `--fluidsolve solve` while the lake still drains, which
                  today's solve row runs only after the flow window. Decide with the water
                  thread's first slice, since both change what a boundary does.
                  **Played 2026-09-09 on a scratch world (`--world=edge`), and Matthew swept the
                  setting himself — 2, 0, 4, 2 across four sessions.** His report: *"the bulk of
                  the water disperses but the puddles linger."* Once the world had a breach (a
                  bite at a lake's bank, tick ~300 of the first session) `--settleafter=2` and
                  `=4` ran **0 passes**, and a 530 kL burst of stabs took ~160 ticks — 80 s — to
                  leave Tier B, the bench's 170 ticks for a spill with the hand-off alone; the
                  lingering puddles are the automaton's creeping edge, the cells the solve would
                  pack. `--settleafter=0` on the same world ran a pass at every one of 2,001
                  boundaries and a 580 kL burst was gone in 20–30 ticks — 10–15 s — at 78,593
                  sheets levelled, 7,325 packed, 219,795 left alone, 10.8 M cells visited in all
                  (5.4 k a pass), the longest pass 40.5 ms. **So the lever has its number: a
                  pour lingers 80 s or 12 s depending on whether a lake somewhere has a hole in
                  it, and the pass-per-boundary arm is affordable at this scale.** Build one of
                  the two levers with the water thread's first slice, or the cadence one before
                  it if the thread slips — it is the smaller change and `--settleafter=0` is
                  already its *N* = 1 arm.
            - [x] **The solve and the hand-off had a seam.** A levelled sheet was asleep without a
                  visit and the hand-off reads visits: 10,558 L handed in the relay's window with
                  the settle off against 4,178 with it on. The solve lists what it levelled and
                  left with no move as resting and runs before the hand-off; with that, `--spill`'s
                  new third arm rests in 29 ticks against 314 and the relay's whole 50,000 L is the
                  basin's inside the profile window, Tier B empty.
      - [ ] **The played world floods the moment it opens — found 2026-09-05 by a mis-invoked
            control.** `default` has 116 ML banked over a mine of 4,562 bitten chunks; with M8b-2c's
            seam its dug chunks file portals on arrival and the terrace ponds drain into the mine at
            ~58 kL a tick — 465,036 L in the first four seconds, 110,792 cells active by nine, ticks
            of 87–188 frames in Debug. The seam as built, on the world it was built for. **The
            play answered the same day: nothing** — he likes it. **For M8b-4**: that water has to
            survive a quit, and its first save is half a million litres.
      - [ ] **"It is going to take a long time for my world to settle" — Matthew, 2026-09-05, and
            it is M8b-3b's case with a number.** The mine is 4,562 chunks over 264 × 304 m; in
            Debug a tick at 110,000 active cells is ~190 frames, so water moves a cell every three
            seconds and ~116 ML of lakes take hours of wall clock to drain in; then the waterline
            of that floor is a sheet of up to ~1.3 million cells, and diffusive levelling across it
            is *never*. One solve over it is ~0.3 s Release, a few seconds Debug, once. **3b is
            priced against this world, not the chamber fixture**, and a Release build matters to
            this world before M9 does. **Priced 2026-09-05 with 3b built: 1–3 µs a cell in Release,
            so that floor is seconds a pass and the pass is one frame — see 3b's slice item.**
      - [ ] **The r6 control's command is quoted verbatim now.** `--nosave` on the engine's side of
            the bare `--` is dropped without a word, Main's warning covers only `name=value` flags,
            and the run opens the played world and reads as a flood. `--headless --quit-after 2400
            -- --nosave` is the control. Consider extending Main's warning to any `--flag` it knows
            the name of.
- [ ] **M8b-4 — the store. Planned 2026-09-06, split three ways; see M8-PLAN's "M8b-4 — the
      planning pass".** Three things the plan's paragraph assumed are not so: water in an unloaded
      chunk is not deleted today, only *awake* water is (the grid's panel stays in RAM and the
      awake cell is displaced into "solid" unloaded neighbours); the store is a mirror of RAM
      before it is a replacement for it; and the played lake is 78 MiB of panels, over the 64 MiB
      trigger, on the first lake the store meets. **Decisions handed back, recommendation first**:
      pause rather than settle on unload; the three slices below in order; the fluid blob on the
      chunk batch as a third list on `IWorldStore.Write`; the blob's sparse form against the raw
      panel, which `--blob` decides.
      - [x] **M8b-4a — the pause and the arrival. ✅ Built 2026-09-06.** Core, no disk. `IFluidTerrain.IsLoaded`, asked
            only where a visited cell cannot hold; a cell whose chunk is unloaded sleeps in place
            and is remembered as paused; at arrival (`onVoxelsReady`, all three paths) the paused
            cells wake, and so do the held cells on the chunk's six faces and the loaded
            neighbours' cells touching them, because a boundary edit while the chunk was away is
            what a pause cannot see. `VerifyQuiescent` skips unloaded cells. `paused` on the
            `M8b:` line; the play's oracle is `displaced` at zero across a walk. Lifecycle fuzz
            with a control arm that never unloads. Recipes to file first are in the plan.
            **Built as written; suite 1,040 → 1,053. Measured in the same headless run with the
            pause taken out (r4h/2v, 12 m/s, turning every 10 s, a 200,000 L relay pour):
            displaced 188,866 L → 0, handed to Tier A 11,134 L → 195,981 L, 20,159 cells resumed
            over 3,761 arrivals, 406 face wakes.** Fourteen recipes, thirteen caught; the miss —
            `IsLoaded` asked before `CanHold` — is an identity, since *not loaded implies cannot
            hold* for every terrain, and the order is a cost claim the suite cannot see. Fixture
            trap for the record: **coordinate zero is a chunk face layer**, so a corridor at
            y = 0, z = 0 was reached by the face wake in every cell. The face wake's two halves
            each have a test that fails without it — the neighbour's for water rested against the
            absent chunk, the chunk's own for the sheet solve, which writes a levelled cell beside
            a non-holdable neighbour without waking across. **A quit still loses paused water:
            4b.** The `M8b:` line's `paused` should read zero whenever every chunk is home; a
            figure that never comes down in a run where chunks are arriving is an arrival nobody
            announced.
            - [ ] **Play check**: walk away from the flooded mine and back, and read `displaced`
                  on the `M8b:` line across the window moves — it should not move at all now, where
                  the 2026-09-05 play read 2.9 ML. `paused` rises as you leave and falls as you
                  return; `resumed` climbs.
      - [x] **M8b-4b — the store. ✅ Built 2026-09-07.** `FluidPanelBlob` on the sky column's frame — cx, cy, cz, the
            held cells as ascending (index, mL) pairs, the paused indices, zlib — validated at
            read; a `fluid` table, schema 2 → 3 additive, header unchanged; the blob rides the
            chunk batch; a dirty bit per panel set by `FluidGrid.Write`, cleared at submission,
            the sparse pairs copied on the sim thread when queued; written at unload, autosave
            and quit, read whole at open into paused panels; `loaded` joins the identity. Oracle:
            the twin of `WalkingAwayAndBackLosesNoWater` across a quit, the blob battery in M6a's
            shape with both halves mutated, the contract suite's third list and its atomicity,
            and a headless `--injectlitres` quit-and-reopen with `held` equal across it. **Last
            and alone, because a defect here is permanent data loss.** **Built as written, with
            three things the plan did not say — see M8-PLAN's "M8b-4b — built": the bench's
            crossover is time not bytes (an all-lake panel is 6.4 ms as pairs, 0.08 ms whole; both
            forms ship, the writer switches at 1,024 cells, an estimate); a row listing awake cells
            is rewritten once they rest, or every open re-wakes them (the fuzz found it); a drained
            panel writes itself empty. Wire: 7,144 L / 1,894 awake at the quit, 7,144 L / 1,894
            paused at the reopen, identity exact; r6 control unchanged. 26 recipes, 25 caught, 1
            identity. Suite 1,073 → 1,134.**
            - [x] **Play check ✅ 2026-09-07, read off the logs**: the played world opened at
                  schema 2 with `loaded 0 L`, flooded, and quit at `2,216,035 L in 220,333
                  cell(s), 212,621 awake or paused` (3,435 panels, 12.3 MiB written); the next
                  open `restored 2,216,035 L in 167 panel(s) (119 empty row(s)), 212,621 cell(s)`
                  in 114.6 ms, identity exact from its first line. **The autosave copy is priced
                  and it hitches, in Debug**: 72–100 ms for 105–197 panels across 31 autosaves,
                  and 399 ms on the first autosave after the restore with all 212,621 cells
                  awake — six to twenty-four frames twice a minute. Not reported by Matthew.
            - [ ] **Lever, not built**: the snapshot copies the held cells as pairs on the sim
                  thread, one panel scan each — 0.5 ms a panel in Debug, measured above. Copy the
                  64 KiB panel whole and extract on the lane. Build when the hitch is reported.
            - [x] **The Tier B surface mesh is the play's largest stall, and nobody priced it.**
                  `FluidSurface` rebuilds on the main thread every 0.25 s while the water has
                  moved: **144,559 quads in 119.7 ms** on the flood after the restore, seven
                  frames four times a second for as long as the flood ran; 38,990 quads at
                  37.9 ms in the 15-minute session. M7d-2's 0.67 ms was Tier A's 739 quads, and
                  this scales with the wet surface. Levers, none built: rebuild only the panels
                  whose water moved (per-chunk surface meshes, the terrain's own shape), or build
                  the arrays on a worker and apply under the mesh-apply budget. Wants its own
                  measurement arm before either — the bare number is from a play log.
                  **✅ 2026-09-21 — the first lever, built**: the arrays went to the worker at
                  M8d-1 and the mesh is per panel now, rebuilt only where something it reads
                  moved (the grid's per-panel stamp, a face neighbour's, the column below, the
                  lake band over it, a previous surface it lerps from); `FluidSurface` keeps one
                  `ArrayMesh` an instance per wet panel and installs only the update. On the
                  bench the lake at rest went 837 KiB / 57 ms a build to 0 KiB / 0.09 ms; a flood
                  costs what it did. **The played-world figure is still owed** — see the item
                  under M10d.
      - [ ] **The water thread — planned 2026-09-07 with Matthew, placed after 4c and before M8c;
            see [docs/WATER-THREAD-PLAN.md](docs/WATER-THREAD-PLAN.md).** Three slices, each
            useful alone: (1) the order — near cells (5×5 chunks, full height) visited every tick,
            far cells deferred past the boundary under a per-tick admission quota, `--fluidnear=` /
            `--fluidfar=` the sweeps, 0 rings the control, judged in play first; (2) the readable
            terrain — a copy-on-write holdability bitset per chunk, ~10 MiB at r6. **Priced
            2026-09-09 with `--terrain` and it does not pay for itself**: the game's read is 1.47x
            Release / 1.59x Debug of a baked field's, which is a third of a visit rather than a
            multiple, so the field is slice 3's *enabler* (the terrain a worker can read) and
            inherits slice 3's revisit trigger instead of standing beside it. **Recommended: both
            wait on slice 1's play.** **Not a copy of the chunks and not a memory saving**
            (4 KiB of bits against ~2 KB of palette-compressed voxels); it replaces a dictionary
            lookup, a palette decode and a material-table read per neighbour, up to six a visit,
            which is why it stands alone if (3) never happens. **One bit behind
            `FluidField.ClassAt`, decided 2026-09-09** — asked at four bits and decided at one,
            because the field is a derived cache rather than a format: nothing on disk, nothing to
            migrate, so M8c widens it to a nibble by changing a build function and an accessor if
            its seepage wants permeability inside the tick. Four bits would be 40.6 MiB at r6
            against 10.2 and four times the working set, on the one axis (locality) the field
            exists to win. A uniform chunk wants one class rather than 4 KiB — M5c-1'''s closed form
            in a second place, and worth building, not naming; (3) the tick as a scheduler job per boundary, mutations
            landing at the next boundary, a slow tick lowering the rate, ~8× the Debug throughput.
            All eight decisions taken as recommended. Build (3) when the play reads the flood on
            open as hurting rather than slow. Not started.
            - [x] **Slice 2 — ✅ built 2026-09-10, decisions 1–4 as recommended.** `FluidField`
                  is an `IFluidTerrain` built from one, so nothing else changed; one bit behind
                  `ClassAt`, 4 KiB a chunk, `--fluidfield=0` the control. Suite 1,199 → 1,206.
                  **0 tick misses on a 40 s flood (80 chunks, 0.31 MiB) — slice 3's precondition,
                  measured.** Three findings: **one ring of face neighbours was wrong** and the miss
                  count staying identical to the digit while the set grew 32 → 80 is what said so
                  (two rings now, 1 + 6 + 18, Manhattan not Chebyshev); **every miss is the pour**,
                  which asks whether a voxel can hold before a panel exists there, so `Misses` is a
                  cost and `TickMisses` is the gate; and **a field is an amortisation that can
                  lose** — 32,768 questions to bake, one saved per read, so baking more than the
                  water uses costs more than it saves.
                  - [x] **Played 2026-09-10 and it was broken; fixed the same day.** *"Is the water
                        supposed to be stuck or frozen?"* — `PortalDrain.Draw` threw at every
                        boundary, before the pour, the sync and the tick, so the settle ran 295
                        times and the tick twice. **The field had baked a chunk whose voxels had not
                        arrived**: `CanHold` answers false for an unloaded voxel, so 32,768 "solid"
                        answers were cached as fact and only an edit could have corrected them.
                        `Build` declines now (`Declined` on the `fluidfield` line), and `IsLoaded`
                        always delegates to the source. Verified on a copy of the played world:
                        ticks 9 → 19 and climbing, `displaced 0`, identity exact. Suite 1,206 →
                        1,208. **The lesson beyond the fix: do not cache an answer whose meaning
                        depends on state the cache does not capture** — three APIs here have a "no"
                        that means two things.
                  - [x] **Re-played 2026-09-10 — *"Yeah I like it"*.** The check was that nothing
                        should look different, and nothing did. Slice 2 is accepted; slice 3 is
                        unblocked and still gated on its own trigger.
            - [ ] **Slice 3 — split three ways on contact 2026-09-10: 3a the grid belongs to the
                  tick, 3b the tick as a job, 3c the race battery and the measurement.**
                  - [x] **3a — ✅ built 2026-09-10, the reversal taken knowingly ("ok lets
                        continue").** Suite 1,208 → 1,216; see the plan's "Slice 3a — built" for
                        the three departures from the stash's recipe. Mutations queue and land at
                        the tick's own completion (not at the next boundary — a `Reclaim` in the
                        gap could outrun a deferred `Evict`); reads refuse; **the pipeline parks a
                        wet arrival while the grid is the tick's and releases it on the first frame
                        after**, because clearing `FluidPending` at arrival let a visit flow into a
                        chunk whose restore was still queued; a dry chunk is ready at once. Alt+R
                        queued, autosave postponed, quit finishes the tick. **Played 2026-09-10 —
                        *"Seems pretty good to me"* — on the played world for 40 minutes: 10,031
                        deferred, 233 parked, `displaced 0`, identities exact, `paused 0`, quit
                        exact. Accepted.**
                        - [ ] **A quit cancels rather than finishes the tick** — a named lever, not
                              built: the unvisited half of the batch would have to go back to the
                              pending set, and at a quit the cost today is one tick's remainder,
                              printed (`M8b: finished the tick in flight before the quit`). Build
                              it if that line ever reads as seconds.
                        - [ ] **A chunk that leaves and returns inside one tick waits for the
                              boundary even when dry**, because its mark is in the deferred queue.
                              Rare (a turnaround inside half a second) and bounded by the tick; if a
                              sprint corridor ever shows meshes waiting on `parked`, the fix is a
                              deferred `Evict` that records no mark for a chunk with no panel.
                  - [x] **3b — the job. ✅ Built 2026-09-10, all nine decisions as recommended
                        ("ok lets continue"); see the plan's "Slice 3b — built".** Suite 1,216 →
                        1,234; 25 recipes, 25 caught. `--fluidjob=1` in `start.cmd`, 0 the
                        sim-thread arm. Three findings: **ownership is the batch or the job**
                        (an empty batch left `TickInProgress` false with a worker on the grid —
                        the fuzz's job arm found it); **`Wake` asked the live terrain from the
                        worker** (found by a two-counting-terrains test); and **the refusal count
                        over-claimed** — on the played world's open 4,754 refusals, of which one
                        for a chunk that had arrived, the rest the live terrain's own "not loaded".
                        Measured: the same trajectory as the sync arm to the visit; both arms at
                        2 Hz on the open at ~18,000 active, the job's worker wall ~50 ms mean.
                        **Played 2026-09-10 — *"I got it to skip slightly but that was all"*:
                        peak 153,250 active at a steady 2 Hz, worker wall 172 ms mean / 319 max,
                        0 boundaries waited, identities exact, 461 drawn. Accepted. The skip is
                        the boundary's 332 ms worst frame — field sync 230, settle 143 — and is
                        3c's first item.**
                        - [x] ~~The first flooded boundary's field sync is 235–279 ms~~ — capped
                              per boundary in 3c (748 → 51 ms on the open as it now is).
                        - [x] ~~The settle's first pass on the open is 114–138 ms~~ — on the
                              worker since 3c, at the job's tail with the order preserved.
                        - [x] ~~`Publish` walks the awake set every boundary~~ — counts since 3c;
                              the quit's line walks once.
                        - [ ] **`MayHoldWater` under a job is an over-approximation** (published
                              panels ∪ the captured set); a chunk that is in the captured set,
                              dry, and arriving — which cannot happen, since captured means
                              loaded — would park needlessly. Recorded, not a defect.
                        - [ ] **`--fluidbudget` means nothing on the job arm** and the setup line
                              says so; the sync arm keeps it. If the sync arm is ever retired,
                              the flag and the slicing go with it.
                  - [x] **3c — ✅ built 2026-09-10, all five as recommended; see the plan's
                        "Slice 3c — built".** Suite 1,234 → 1,240; 14 recipes, 14 caught.
                        `--fieldbakes=<n>` caps the bakes (8); the settle's solve runs on the
                        worker; `Publish` counts rather than walks. **The open's worst field sync
                        748 → 51 ms, settle 0 on the sim thread.** The race battery found a real
                        3b hole — a departed chunk's field stayed in the worker's view, so its
                        queued cells were moved rather than paused and the eviction released
                        them unlisted — and `Invalidate` baking an unloaded voxel as rock; both
                        fixed and pinned. Moving 2 ML flood: generation identical in both arms,
                        `first draw` 6.08 s against 8.72, 2.1× the visits. **Unplayed: open the
                        played world, bite under a lake, read `boundary worst` on the `M8b:`
                        line and whether the skip is gone.**
                  - [ ] **Lever, named 2026-09-15 and not built — the tick across workers (Matthew:
                        *"each chunk's water calc in its own thread?"*).** Colour waves over
                        chunks as N detached jobs a wave, per-panel counters, per-job wake lists,
                        no panel release mid-tick, and a wave-order oracle in place of
                        sliced-equals-whole; not halos, because the 16-cell fall crosses faces
                        inside a tick. **Trigger: `boundary(ies) waited` on the `M8b:` line
                        non-zero routinely** — the visit loop is worker time nobody waits for
                        (45 ms Release a 150k-cell tick against a 500 ms period) and the felt
                        skip is the sim-thread boundary. Reasoning in WATER-THREAD-PLAN's revisit
                        triggers.
                        - [x] **Played 2026-09-10**: worst frame 332 → 63.7 ms (field sync 43.9,
                              hand-off 24.7, pour 17.5, settle 0), one boundary waited over a
                              128,600-cell peak, identities exact. Accepted on the numbers.
                        - [x] **The surface install is the largest main-thread term left**:
                              7.84 ms for 20,196 quads every tick, beside a walk the worker now
                              does in 9.09. Build the vertex arrays on the worker; only
                              `AddSurfaceFromArrays` has to stay on the main thread. **✅ M8d-1
                              built the arrays on the worker; 2026-09-21 made the install
                              per panel and only for the panels that moved** (the M8b-4b item
                              above, and the measurement owed under M10d).
                        - [ ] **Tier A stands at 387.8% of capacity** on the same play (41 ML in
                              basins of 10.7 ML, 648,611 L "would not fit back") — the hand-off
                              feeds a basin past its rim. Does a lake read higher than its bank?
                              M8c's, since seepage is where the rims start to matter.
                        - [ ] **Bake fields as jobs** — the named revisit if the cap's wait
                              (refusals for arrived chunks; 394 on the open) is ever seen in play.
                        - [ ] **The capture and compose is 3.9 ms a boundary at 200,000 active** —
                              the batch's composition on the sim thread. Fine at that size; the
                              lever is composing on the worker from a published pending list.
            - [ ] **Slices 2 and 3 — ✅ planning pass done 2026-09-10, eight decisions handed back
                  with recommendations; see the plan's "Slices 2 and 3 — the planning pass". Build
                  slice 2 once they are answered.** What the pass changed: **the field is an
                  `IFluidTerrain` built from one**, so the tick, every fixture and every bench arm
                  are untouched and the A/B is two terrains rather than two code paths; **the
                  reachable set is the chunks holding awake cells plus their face neighbours**,
                  known at the boundary, so 1–2 MiB rather than 10 (an estimate); and **the plan
                  named six seams where there are thirteen, and the ones it missed are reads** —
                  the autosave's `TakeAllDirty` is the sharp one, since a 30 s timer can land inside
                  a tick that outlasts hundreds of frames. Two things that came out better than
                  expected: **cancelling a tick mid-flight is already safe**, because the cursor
                  leaves the grid consistent between cell visits, so quit needs no new invariant;
                  and **the surface walk rides the job** rather than needing a snapshot. The old
                  item:
            - [ ] ~~**Slices 2 and 3 want one planning pass together, before any code (2026-09-10).**~~
                  **Slice 3 cannot be built without slice 2**: its job is submitted *"with the
                  captured terrain"*, and that is slice 2's field, because the tick asks `CanHold`
                  and `IsLoaded` of the streamer's chunk dictionary and a worker cannot read it
                  while the sim thread adds and removes chunks. So "slice 3's single worker" is
                  really slice 2 then slice 3. Three of slice 3's inputs have moved since it was
                  written: the budget's ~8x is measured, **the field is lazily-wet rather than
                  10 MiB of view** (the tick only asks about wet chunks and their shell — the played
                  world holds 89 wet rows, so one to two MiB; *an estimate*), and the surface walk
                  can ride the tick job rather than needing a snapshot of its own.
            - [x] **Slice 1 — planned and ✅ built 2026-09-09, unplayed; see the plan's
                  "Slice 1 — the planning pass" and "Slice 1 — built".** Suite 1,179 → 1,196,
                  14 recipes and 13 caught (the miss an identity).
                  - [x] **Play check — done 2026-09-09, and it found the lever is elsewhere.**
                        *"The water i drop using r tend to stay still but I noticed that water
                        nearby seems to be gathering the runoff."* Read off the log: the throttle
                        works (30,000 far cells deferred, steady), the hand-off works (`handed`
                        climbing, `received == handed: exact` — that is the runoff being gathered),
                        and **the water still moves at ~1 Hz because a tick is ~39,200 visits,
                        ~114 ms of Debug CPU, ~57 frames at the 2 ms budget, worst 292.** The quota
                        is relative to the near set, so a near set of ~19,600 cells makes a tick
                        2x that whatever the far side does — **slice 1 cannot bound a near set
                        that is itself 28x what a Debug frame buys.** And **the settle ran 0 passes
                        in 340 ticks**, because a portal draws every tick and a draw is inflow: the
                        treadmill, third sighting, and now the largest lever on this world. The
                        periodic `M8b:` line now prints `last tick N near + M far` so the next play
                        need not infer it from visits-per-tick.
                  - [x] **The settle's quiet counter — built and played 2026-09-09/10.** Matthew:
                        *"I cannot fault it in any way so perfection as far as i'm concerned."*
                  - [ ] **Judge slice 1 again on a world whose active set is not 70,000 cells** —
                        this play could not separate it from the settle change.
                  - [x] **The Tier B surface mesh — ✅ 2026-09-10, and it was not a threading
                        problem.** Splitting the timer first said the walk was 25.40 ms of 28.38 and
                        the install 2.98; the walk was asking the grid per held cell where Y being
                        contiguous makes the cell above the next array index.
                        `FluidGrid.SurfaceCells()` is that walk, pinned against the naive definition
                        by a fixture that stacks two bodies in one column and straddles a panel
                        boundary. **28.38 → 12.09 ms (walk 25.40 → 8.85), so 114 ms a second of main
                        thread → 48.** Threading it is now optional and **wants slice 3 first**: the
                        tick job already has exclusive access to the grid, so the walk rides it with
                        no snapshot. Superseded item:
                  - [ ] ~~**The Tier B surface mesh goes to a worker, ahead of slice 3.**~~ 33.08 ms for
                        19,454 quads on the main thread every 0.25 s while water moves
                        (`FluidSurface.RebuildIntervalSeconds`) is **132 ms a second against the
                        water tick's whole budget of 120** — and it is a pure function of the grid,
                        so it needs no concurrency argument. Named 2026-09-07, given its second
                        reading and its ordering 2026-09-10 when Matthew asked about four threads.
                  - [ ] **Four threads on the automaton: recommended against for now, with the
                        arithmetic in the 2026-09-10 worklog entry.** One worker is ~8x (the
                        rationing is `--fluidbudget`, not the core); four is 4x on top of a problem
                        one over-solves; only the roof-open case (302,182 cells, ~876 ms Debug)
                        would use them, and DESIGN licenses that being slower than realtime.
                        **The cost is the automaton's only oracle** — the batch is a `List` so that
                        "the same input settles the same way" is a property rather than a
                        coincidence — so a correct four-thread design needs a deterministic spatial
                        partition with serialised boundaries. Revisit when a play reads the
                        roof-open case as the thing that hurts.
                  - [ ] The original recipe, for a re-run: `start.cmd` passes `--fluidnear=2`. Open the played world,
                        stand where water is flooding, and stab `R` at your feet. The question is
                        whether the pool under you keeps moving while the mine fills. Read
                        `fluidnear` on the profile line and `far deferred` on the `M8b:` line;
                        `--fluidnear=0` is the control. **The headless instrument cannot answer
                        this** — `--injectlitres` pours at the player, so the throttle bound on 0 of
                        78 ticks at 4 m/s.
                  - [ ] Not covered by a test: that the carry is drained from its front. FIFO
                        against LIFO only bites under a growing backlog and there is no cheap
                        boolean for it; progress every tick is asserted instead.
                  The pass's own record: The pass corrected the mechanism twice: **a sort delivers
                  nothing**, because a tick does not end until its batch does, so near water would
                  move once per 188-frame tick and not at 2 Hz — far cells have to be *deferred
                  past the boundary*; and **the far floor is cells admitted per tick, not a share
                  of the frame budget**, because a budget-driven interleave would cost the
                  whole-equals-sliced identity the tick budget slice bought. The shape is a partition
                  in `Begin()` — admit `near + max(512, near)`, nearest-first with one part in
                  eight of the quota rotating — over a deferred list held **out of the swap**,
                  because putting it back in `_pending` costs an O(backlog) walk every tick to
                  admit the floor (~2 ms Debug on the 110,000-cell flood, against 1.5 ms of far
                  visits): Matthew, 2026-09-09, *"the cross handling overhead, makes it less than
                  optimal?"* The carry stays in `_queued`, which is what keeps `AwakeHeldCells`
                  and the save path right; `ActiveCellCount`, `ActiveAfter`, `Settle`'s loop and
                  `Pending` are the four readers that change. **With no near
                  water awake the tick is today's tick**, so the blast radius is the case the play
                  is about. Three costs stated: the cliff at the first near cell; the boundary work
                  (settle, hand-off, drain) going back to 2 Hz during a far flood, which is the
                  rate it was priced at and makes the treadmill turn faster — the settle pass on a
                  162,572-cell sheet is the figure that would reverse it; and the carry's
                  reclassification, which is exact if it runs only when the viewer's chunk moved.
      - [x] **M8b-4c — the panel leaves RAM with its chunk. Planned 2026-09-07 and ✅ built the
            same day, all six decisions as recommended; see M8-PLAN's "M8b-4c — the planning
            pass" and "M8b-4c — built".** Suite 1,134 → 1,158; 12 recipes, 11 caught, 1 identity
            (the flag surviving the cache — a revival re-derives it). **One thing the pass did not
            say, found by the played world on the first probe: the portal tracker had to move
            onto the fluid-ready hook too**, or the drain pours into a chunk whose row is still
            pending and throws — to everything that is water, a chunk arrives when its water is
            home. Measured on a copy of the retired played world against the previous build:
            panels in RAM at the end 364 (22.8 MiB) → 81 (5.1 MiB), 256 rows read off the lane
            in 122 ms, first draw 10.1 → 10.3 s, identity exact with `evicted`; the bench's
            evict arm rests where the keep arm rests, the visits differing by the arrival wakes
            alone, a row 280 µs to write and 152 µs to read (Release). The r6 control unchanged.
            - [x] **Play check**: `start.cmd` on the fresh world, pour with `R`, fly away and
                  back, quit and reopen. Read `evicted` and `loaded` on the `M8b:` line, `paused`
                  at zero once every chunk is home, `displaced` at zero, the identity `exact`; the
                  `M8b: the world file holds` line under `at quit`, which the next open's `loaded`
                  reaches once the wet chunks are home; and the `M6:` line's `fluid N row(s) …
                  loaded off the lane (ms)`. What a person can see and the log cannot: a lake
                  that pops in a beat after its terrain, and any water that looks wrong at a
                  chunk face after a turnaround. **Read 2026-09-07 across two sessions**: the
                  first quit at `936,458 L in 58 row(s)` and the next open's `loaded` reached
                  936,458 L exactly, then 965,492 once two evicted panels (29,034 L) were read
                  back — `loaded − evicted` is the file; 58 rows off the lane in 10.2 ms; `paused`
                  0 on every report of both sessions; the identity exact throughout. **`displaced`
                  was not zero and it was never the walk**: 401,977 L in the first session and
                  3,693 in the second, each step landing on the report where `M6c:` gained a
                  chunk fill — 7,925 voxels → 170 L, 67,346 → 339,191, 100,114 → 401,977. A fill
                  into a flooded chamber deletes the water it fills, reported and not conserved;
                  whether it should push the water aside instead is a design question for M9's
                  placement pass, with a third of a megalitre as its first number. Matthew's
                  reading of the look: *"I think its good?"* — nothing reported wrong at a face.
            - [ ] **Lever, not built: `FluidGrid.Restore` writes a lake panel cell by cell** —
                  ~32,768 `Write` calls on the sim thread inside the apply, the p99 frame 33 →
                  58 ms on the played-world probe. A bulk restore of the dense form with the
                  totals recomputed once is the fix; build it when a restore is felt as a hitch.
            - [ ] **`viewer ms` is 60–80 ms a move on the played world with water on, in both
                  arms** (83 ms mean at HEAD, 61 at 4c; 1.5 ms with `--nowater`), so it is not
                  this slice's: the box sweep with the portal tracker's scans and the arrival
                  wakes inside it on cache revivals. M6d-2 measured the sweep at 7–13 ms and did
                  not chase it; with water it wants its own pass — the scans on a worker, or the
                  arrival work deferred out of the sweep.
            **The pass, kept:** five things the paragraph assumed were not
            so, two of them the whole slice: **the panel lingers one tick after the forget**, so
            the tick pauses the chunk's queued cells and the row lists them (released at the mark,
            a flood front inside a leaving chunk is stranded on every walk); and **a chunk whose
            row is still to be read is not loaded, to the water** — a `FluidPending` flag the
            fluid terrain reads through the streamer, the restore as a chunk-slot job on the
            serial lane for all three arrival paths, and `ChunkArrived` moved to a new
            `onFluidReady` that fires after the restore. Memory, the evicting set, the snapshots
            not yet written, then the store's index — the wrong order restores the past and the
            identity cannot see it. `evicted` joins the identity; `_storedAwake` must forget an
            evicted chunk or the next autosave deletes the lake; nothing in the cache (a hit costs
            one 0.1 ms lane read; the blob is the lever). Priced on a `--evict` bench arm over the
            lake fixture and the turnaround on a played-world copy; `PanelBytes` falls from the
            world's wet chunks to the box's. Six decisions handed back, all with recommendations.
            Revisit trigger stands: `PanelBytes` past 64 MiB in a play.
      - [ ] The unload-time settle is not called by any of the three. `LevelSheets(seeds, share:
            0)` stays as the API — with a share of zero, because the share slice's early stop
            assumes every unsettled cell neighbours a seed, which the wake rule guarantees for the
            active set and nothing guarantees for a chunk's cells.
      - [x] **A walk deletes the flood's awake water — closed by M8b-4a on 2026-09-06: displaced
            188,866 L → 0 L of a 200,000 L pour in the headless turnaround, and the quit's loss is
            4b's. Read off the 2026-09-05 play log: 2,924,347 L
            displaced of 7,163,284 L drawn in one session, 41%.** An unloaded voxel is solid, so an
            awake cell in a chunk that leaves the box is displaced on its next visit into neighbours
            that are unloaded too, and drained; every jump in `displaced` landed on a window move,
            with zero placements in the run. A sleeping cell survives and comes back with its chunk;
            a flood is awake. The plan stated the quit and wrote "a chunk with settled water" on
            unload — **the settle (3b) is what makes an unload a hand-off rather than a deletion, so
            its unload method is on this item's path, not beside it.** Tier A dropped 1,855,295 L
            "would not fit back" over the same six window moves, the first time that figure has
            read above zero. **Again in the M8b-3b play the same evening: 1,865,495 L of
            2,494,274 L drawn, 75%, over 44 window moves** — and 40 L a report with the player
            standing still, the awake cells at the loaded edge.
      - [ ] **The water line's `% of capacity` sums leaf basins, so a merged lake reads over 100%
            (525% on the played world).** A label, not the litres; fix the denominator to the
            tree's capacity or drop the percentage before anyone reads it as a defect.
- [x] **M8d — planned 2026-09-11; see M8-PLAN's "M8d — the planning pass". Ten decisions, all
      taken as recommended the same night (*"As planned please"*); all four parts built and
      played the same day, and closed on Matthew's verdict: *"Look I love what we have, it
      settles."*** The pass's spine:
      "more 3D" is three absences in the debug surface — no side faces, no depth in the colour,
      no motion between ticks — and the surface install is already the largest main-thread term,
      so the mesh is rebuilt as merged tops plus sides on the worker before anything is polished;
      the waterfall is Matthew's fall rule (stateless, up to 16 a tick) with sides to give it a
      front; the lake-bank treadmill is the hand-off refusing a cell under its own lake.
      - [x] **M8d-1 — the mesh. Built 2026-09-11; read M8-PLAN's "M8d-1 — built".**
            `FluidSurfaceMesher` in Core: tops merged within a 2 mm tolerance, sides between drawn
            heights wherever water meets air or a lower fill, bottoms under falling cells, column
            depth and previous height per vertex in UV; built on the worker, installed on the main
            thread; `--fluidsurface=debug|nosides|nobottoms|notops|nomerge` the arms, `--surface`
            the bench. A lake at rest installs 26× fewer quads, a flood in progress 1.5–5× more.
            - [ ] **Seam slivers.** A side between two runs drawn a millilitre apart across a
                  panel seam, because the neighbour's drawn height is in another panel and its held
                  amount stands in: 2,231 on the jittered lake fixture, none visible (a millilitre
                  is 0.016 mm). Lever: keep the last panel row's drawn amounts. Trigger: a play
                  that reads the seams, or the quad count mattering.
            - [ ] **A covered cell is drawn full**, so a falling column is a shaft and not a stack
                  of slabs. A rendering rule that exaggerates falling water's volume; judge it with
                  M8d-3's fall, where the falling picture changes anyway.
            - [ ] **The mesh costs one to three times the walk on the worker** (19–48 ms Release on
                  the played lake's shape against the walk's 10–37; 43–44 ms Debug on the played
                  world's open). Fine on the job arm; on `--fluidjob=0` it is sim-thread time at
                  every rebuild, and the first lever is skipping the terrain ask for a floor cell
                  the previous build already answered.
      - [x] **M8d-2 — the look. Built 2026-09-11; read M8-PLAN's "M8d-2 — built".**
            `FluidWater.gdshader`: depth darkening from UV.y, the `tick_phase` lerp from UV.x,
            depth written and back faces culled (the mesh's winding flipped for it), sides tinted
            by normal, the underwater wash off the installed mesh. Screenshots sent; the verdict
            is Matthew's, in play.
            - [x] **Play, M8d-4's fourth question — answered 2026-09-11: *"Looks great …
                  reflective and all, no expense spared"*** on the played world's flooded mine,
                  the pit under the lake and the eye in the water; the log behind it is in
                  WORKLOG. The look constants (shallow and deep colours, falloff, tints, the
                  wash's alpha) are `FluidWater.gdshader` uniforms and the tint's `Color` in
                  Main; retune by number if a later play asks.
            - [ ] **One unnamed failure in `FluidJobTests` on the run straight after the M8d-2
                  commit** (32 tests in 13 s, one failed, the harness line cut the name off), not
                  reproduced in three re-runs of the class nor in the full suite after. Recorded
                  rather than dismissed; if it returns, read the name and diagnose the mechanism
                  from the code before touching the test.
            - [ ] **From inside the water nothing of it draws** (cull_back) — a swimmer looking up
                  sees no surface. Wading (M10-NOTES) will want the underside drawn from below:
                  a second, front-culled pass of the tops, or cull_disabled with sorted faces.
                  **Seen from below rather than inside, 2026-09-19** — *"Can you examine what i'm
                  looking at"*: a mined void under a lake at 659% of its rims, 17.3 ML of Tier B in
                  1.1 M cells cascading into it; from underneath each resting sheet is its sides
                  alone (19,226 sides against 8,313 tops, 27 bottoms), so the flood reads as a stack
                  of paper-thin slabs with the Tier A plane showing through as flat pale blue and
                  the fronts' fractal edges as dark side-tinted patches. Every identity exact. The
                  same item, from the other side; the fix is the same pass.
      - [x] **M8d-3 — the fall and the bank. Built 2026-09-11; read M8-PLAN's "M8d-3 — built".**
            A visit falls through vacant air up to `--fallcells=` (16; 1 is the old arm), the
            mesher's previous-height search reaches as far, and the hand-off leaves a rested cell
            standing when its sample's lake stands above the cell's top. The front's shape and
            the mine's drain rate do not move under the rule — the look does, and that is play's.
            - [x] **Play (M8d-4), the waterfall — answered 2026-09-11: *"Looks good to me"*** on a
                  lake falling through the mine's ceiling into the flooded chamber, caught tick by
                  tick in twelve screenshots; 46,745 long falls over 417,467 cells on the log (8.9 a
                  fall), identities exact, 0 boundaries waited. WORKLOG has the numbers.
            - [x] **The bank rule in play — read 2026-09-14, and it is the wrong rule.** Matthew,
                  fresh world, no relay: a channel from a higher lake into a lower one; `drawn
                  32,936 L`, `handed 5,389 L`, `27,552 L` left as Tier B under `56,270 cell-ticks
                  left under a lake` — two surfaces, the plane over a stepped sheet on the bed.
                  Decision 8's recorded cost, met by the game's own loop. Replaced by M8d-5.
            - [ ] **The waterfall is the mesh's dear case: 51,087 quads (36,592 sides, 5,443 bottoms,
                  7.6 MB) installed in 22–32 ms a tick against 5–6 for the lake.** A falling column
                  is a stack of cells each at its own depth with air on four sides and beneath, so
                  nothing merges. Two levers, neither built: drop the depth from a side's merge key
                  (the shader's depth term is a top's; sides could carry the column's top depth and
                  merge vertically along the fall), and draw a bottom only under the lowest wet cell
                  of a column, not under every cell with air beneath. Trigger: a waterfall that
                  stutters, or the install passing a frame.
            - [x] **Tier B water under a lake stays — trigger fired 2026-09-14** (*"the topmost
                  sheet of water does not fall in line with most of the water cells underneath"*).
                  See M8d-5 below.
      - [x] **A silent pipeline stall after floating out of the vertical box and back (2026-09-14,
            evening, on `main` at c5cc04a) — found and fixed the same night.** It was the M8d-5b
            wake in `onFluidReady` reading `_fluid.Grid`, which refuses while a tick job is in
            flight; the hook fires mid-tick for every *dry* arrival (3a parks only the wet ones),
            the throw unwound `Complete` before `PushWithNeighbours`, and the chunk sat at
            `Generated` with nothing to push it — the M2c-2 stall exactly. The stall session's log
            had 85 of the refusal under its reports; the cache hypothesis below was wrong (the
            cache only made a vertical trip revive a slab of dry chunks at once). Fixed with
            `MayHoldWater`, the question 3a built to be asked mid-tick: 62 → 0 refusals on a
            30 s turnaround over a copy of the played world. WORKLOG 2026-09-14. The record as
            it was written, kept for the symptom: Matthew flew high
            over a valley (the floor beyond r2v, undrawn by design), came back down, and stood on
            undrawn ground with cave interiors showing black through it — *"I'm standing on air"* —
            for well over twenty seconds. The log's reports were identical for the whole time:
            `2827 tracked, 396 drawn, 0 in flight, 0 queued, 0 faulted, light settled`, the `M2a:`
            sweeps all empty, `cache: 7672 hits (7151 lit), 7146 reseeded`. Idle, not busy: the
            M2c-2 class of stall (a chunk one stage short with nothing left to push it). Hypothesis:
            a chunk revived from the retained cache *with its light grid* (the reseed path, cache
            slice 2) is not pushed on to mesh in some case that a whole slab leaving and returning
            vertically produces and a horizontal walk does not. Reproduce headless: `--profile` with
            a flythrough that climbs 40 m and descends (no such arm exists — `--fall` drops from
            height but never climbs), or a windowed play that does the same; read the kept log
            (`godot2026-09-14T*.log`, the last of the day) for the chunk states. Recovery is a
            relaunch.
      - [ ] **A hook that throws inside `ChunkPipeline.Complete` eats the neighbour push, and
            nothing counts it.** The generate branch runs `onVoxelsReady`, `Support.Arrived` and
            `ArriveFluid`'s `onFluidReady` before `PushWithNeighbours`; one throw from any of them
            is the M2c-2 stall with `faulted 0` and every report idle — found 2026-09-14 when the
            M8d-5b hook read the grid mid-tick. Lever, not built: push before the hooks, or catch
            a hook's throw, count it on the `M2c:` line beside `faulted` and carry on. The second
            keeps the world drawing under a bug; the first is one line and keeps the throw loud.
            Trigger: the next hook added to an arrival path, or the next idle-looking stall.
            **Second instance, 2026-09-14 evening:** `OnFluidTickCollected` threw inside
            `Apply` under the quit's `WaitForDetached` (installing a completed tick's surface
            into the disposed `FluidSurface`), unwound `_ExitTree` past `_pipeline.Dispose()`,
            and two sessions lost their last 30 s with no line saying so. Fixed locally — the
            field nulled, the wait in a `try` — but the frame's pump has the same shape: a
            detached job's callback throwing inside `Update`'s result pass. Same lever.
      - [ ] **M8d-5 — the lake is a body Tier B cannot enter.** Planned 2026-09-14 from the play
            finding, eight decisions in M8-PLAN's "M8d-5 — the planning pass", all taken as
            recommended (*"Lets build it now"*). Split at the Core / wire seam on *"break into
            smaller parts"*.
            - [x] **M8d-5a — the rule, the hand-off and the tests, in Core. Built 2026-09-14.**
                  `LakeCeiling` / `LakeCeilingView` / `LakeBoundedTerrain`, `IFluidTerrain.InLake`,
                  `FluidSimulation.Submerged`, the hand-off's `PlaneAt + 1` and the submerged pass;
                  the bank rule out. Suite 1,359 → 1,367; 17 recipes, 16 caught, one identity.
                  The treadmill's shape in both arms is the headline test. Read M8-PLAN's
                  "M8d-5a — built".
            - [x] **M8d-5b — the wire. Built 2026-09-14** against the list below; read M8-PLAN's
                  "M8d-5b — built" for the three oracles and the storage change `--spill` forced.
                  What is still play's: step 10. Decision 7 (step 6) was reversed by the first
                  screenshot the same evening — the plane draws over a bite and Tier B draws no top
                  under a lake cell; `--lakeoverdug` is gone. The brief, as built:
                  1. `Main.SetUpFluid`: `_lakeCeiling = new LakeCeiling(_seedHeights, _water)` when
                     there is water; wrap the terrain the simulation is given —
                     `new LakeBoundedTerrain(field ?? terrain, ceiling)` — and pass the ceiling to
                     `SurfaceHandoff`. The seep drain and the portal index keep the bare
                     `VoxelFluidTerrain`.
                  2. The job arm: where `_field.Capture()` is handed to `BeginTick`, hand
                     `new LakeBoundedTerrain(view, _lakeCeiling.Capture())` instead, and have the
                     collection unwrap `Inner` for the view's refusal counters
                     (`FluidSimulation.Job.cs`, `_capturedView = captured as FluidFieldView` → try
                     `(captured as LakeBoundedTerrain)?.Inner` first).
                  3. The boundary, decision 6: after `SweepCollapse()` and before the field sync,
                     `_lakeCeiling.Refresh(_fluidReachable, _fluid.Disturb)` — `ReachableChunks`
                     is already filled for the sync; fill it before the refresh on the
                     `--fluidfield=0` arm too. Its own bracket, `lake`, on the `boundary` line and
                     `boundary worst`.
                  4. The arrival: in the pipeline's `onFluidReady` handler beside
                     `_fluid.ChunkArrived(chunk)`, `_lakeCeiling?.WakeBand(chunk, _fluid.Disturb)`.
                  5. Lines: the `M8b:` line gains `lake N column(s), M rebuilt, W woken`; the
                     profile's `handoff` line already prints the submerged terms; a `lake` term on
                     the `fluidfield`/`boundary` lines with the refresh's ms.
                  6. `--lakeoverdug=<0|1>` (1 the pre-M8d-5 arm) and decision 7 in `WaterSurface`:
                     skip a wet sample all of whose 64 columns' open height is below the level,
                     read only for samples whose chunk column the portal tracker marks dug.
                     A/B by screenshot on a copy of `default` (bite a chunk at a lake's edge with
                     `Q`, `--settle=20000`).
                  7. Bench: `--spill` builds a `LakeCeiling` over its `WaterSystem` and wraps
                     `SurfaceTerrain`; record its rows before and after. `--fluid` and `--front`
                     untouched, and reproduce.
                  8. Oracles, verbatim: the r6 control (2601 / 325 / 3446 at 411, every new term
                     zero); the headless pour `--profile --radius=4 --vradius=2 --speed=12
                     --turnaround=10 --measure=30 --injectlitres=200000` with `handed` ≈ the pour
                     and `submerged` counted; the played `default` opened — its 27,672 L of bed
                     water should be handed at the first boundaries (`M8b:` `handed` climbs by
                     ~27 kL, `inside a lake` non-zero) and then stay flat.
                  9. Docs: M8-PLAN "M8d-5b — built", WORKLOG, CLAUDE.md's pick-up and its flag
                     paragraph, DESIGN's M8 row if it states the bank rule.
                  10. Play: Matthew's channel between two lakes on a fresh world — one sheet.
            - [x] **Play (M8d-5) — done 2026-09-14, evening: *"this version seems working now?"*, then
                  *"record it as played"*.** Two pours channelled into a bitten pit on a fresh
                  `default`: 240,000 L injected, 241,204 L handed, 53 cells found inside a lake,
                  73,604 L drawn into the pit against 3,263 on the same play at f3d18d1 (where 47.6 kL
                  sat on the bed under `173,917 cell-ticks left under a lake` — the bisect that put the
                  second sheet on M8d-3's bank rule, not M9c); `17 under a lake` on the surface term.
                  The recipe as it stood:
            - [x] The channel between two lakes on a fresh world — one sheet, the
                  lower lake's plane, nothing stepped beneath it; `R` onto a lake spreads on its
                  surface and vanishes at the next boundary; a `Q` bite in a lake's bed fills
                  from the portals to the seed, the plane draws over it and Tier B draws no top
                  under it — the bite reads as the lake with its walls visible through the water
                  (the 2026-09-14 screenshot's dark box is the before). Read `inside a lake` and
                  `lake … woken` off the `M8b:` line, and `N under a lake` off its surface term.
            - [ ] **The wrapper costs the bench 2.2× a tick and the game nothing measurable** —
                  `--spill`'s inner is a 5 ns bitset and the second interface dispatch is the whole
                  cost. Lever, not built: fold the band read into `FluidFieldView.CanHold` directly
                  (one dispatch) if a Release game measurement ever shows the visit moving.
            - [ ] **A film one cell above the level inside a bite, from a pour** (M8d-5's residue):
                  Tier B poured into a full bite stands at `top + 1` in dug cells, which are not on
                  the plane. Cosmetic, debug relay only. Trigger: a play that reads it as wrong.
      - [x] **M8d-4 — the play. Done 2026-09-11 over one evening's sessions on the played world:**
            "more 3D" — *"Looks great … reflective and all"*; water or spreadsheet at 2 Hz — the
            waterfall, *"Looks good to me"*; dread or inconvenience — a lake let into the mine
            through three openings at once, the galleries filling in steps, and the verdict on
            the whole: *"Look I love what we have, it settles."* Recoverable is M10's pump and
            was not asked. The bank rule's in-play count is the one check still owed (above).
- [ ] **M8d — the transparent pass has its first play verdict before it is planned (2026-09-04).**
      Matthew, on the relay's water stepping down a terraced hillside: *"looks cool going down the
      stairs"*, and then *"it would be nice if eventually it's a bit more 3D, like it's a very flat
      affair right now, but I assumed this would happen in later steps."* It does: M8b-1's surface
      is a debug surface, one flat quad per cell at its fill height, and M7d-2's lake is one plane
      per basin — both drawn so the water could be *seen*, not so it could be looked at. What
      "more 3D" wants from M8d, read off the screenshots: **the vertical faces where water meets
      air on a step**, so a sheet has a lip and a fall has a front; depth in the colour, so a
      puddle and a pool differ; and the waterfall question the plan already names, since a sheet
      stepping down at 2 Hz is the case *slower than realtime* has to survive. Goes in beside M8d's
      three play questions as the fourth. Not a request to start it — his words were "don't let
      me stop you."

#### Slower than realtime is accuracy, not approximation — Matthew, 2026-09-01

Raised before M7 or M8 is planned, and it is the same observation as
[deferred collapse](#deferred-collapse) one milestone earlier: *"I'd be happy if water is 'less
than realtime' because it does work like that, it usually takes a few minutes of rain to fill the
lower levels of a large cave."*

**This is a project-level principle rather than an M8 tuning note, and it should graduate to
DESIGN.md at M7's planning pass.** — **done 2026-09-01: it is a locked decision in DESIGN.md, and
M7b's oracle is its cash value.** The detail stays here; the one-line commitment lives there. The whole antagonist stack — rain, then water, then saturation,
then loss of cohesion, then collapse — is made of processes that are slow *in the world*. A
simulation that runs slower than the frame rate is not cutting a corner; it is matching the
phenomenon. **Twice now the honest answer to "can we afford this?" has been that the expensive
version was also the less accurate one.**

What it buys, and the first one is again the reason:

- **Falling behind stops being a correctness failure.** This is the same structural win deferred
  collapse gets: if the water tick is allowed to be late, a skipped tick means the water arrives a
  moment later, which is indistinguishable from the design. Every budget in this engine so far has
  had to be argued against a correctness risk; here there is none to argue against.
- **The tick rate can be far below the frame rate.** M10 already specifies a fixed 20 Hz sim tick
  for machines; water wants something much slower again, and the tiered design in M8 was already
  built for it — Tier B cells, Tier A bodies, Tier C a coarse field.
- **It legitimises the seam M7 has already designed and M8 would otherwise have to defend.**
  Tier A basin accumulators with **volume→level curves** are an *analytic* model: given a rate and
  an elapsed time, the level is arithmetic. **A slow process described by a rate can be integrated
  forward when nobody is watching**, so cells simulate in view and accumulators integrate out of
  view — and a player who flies away mid-fill and returns finds the right water level without
  minutes of simulation having run. That seam reads as a cheat if water is "supposed" to be
  realtime and reads as the correct implementation if it is not.

Three honest caveats, because the saving is not where it first appears:

- [ ] **Slow is not free, and the active-cell set is still the thing to engineer.** A cave filling
      over minutes has cells active for minutes. **The tick *rate* is what this observation buys;
      the set *size* is still bounded by M8's sleep/wake rules**, and those do all the same work
      they were always going to.
- [ ] **There is a perceptual floor, and it is near the player.** Water filling a cave over
      minutes is right; a water surface two metres away that visibly jumps once a second is not
      slow, it is broken. **Tier A carries a level, so the renderer can interpolate between ticks
      for nothing** — worth deciding at M8 rather than discovering.
- [ ] **Water state now has to survive a quit, and M6 is why that is a real question.** A fill in
      progress is state whose only copy is on disk. The analytic model above is also the cheap
      answer here — store the accumulator and the timestamp, not the cells — but it wants saying
      out loud, and it is a header field.

- [x] **M8c — Tier C, groundwater and seepage. Planned 2026-09-10; see M8-PLAN's "M8c — the
      planning pass". Eight decisions, all taken as recommended 2026-09-11 (*"M8c as planned"*);
      all three parts built the same day, unplayed.** The pass's finding:
      the water table under a lake *is* the lake, so Tier C is derived from Tier A's level and
      stored nowhere — no header change — and seepage is the portal drain through intact rock with
      a permeability factor. **The 388% figure is `WindowCapacityLitres` summing leaf capacities**,
      by its own comment; a merged lake reads over 100% by construction, so the rim question stands
      as a play question and the number is not its evidence.
      - [x] **M8c-1 — the seep index and the measurement. Built 2026-09-11**; see M8-PLAN's
            "M8c-1 — built". `SeepIndex` beside `PortalIndex`, the definition over every scanned
            voxel as the fuzz's oracle, the edit path a second copy of the geometry;
            `MaterialTable.Permeability` refused outside [0, 1] at load and NaN sealed at use;
            `ISeepTerrain` on `VoxelFluidTerrain`; `PortalTracker` feeds both indices; `--seep`
            on the bench. **Measured, Release**: a solid chunk scans in 4.65 ms (2.65 of it is
            32,768 reads at 77 ns), a bitten one 3.5, the room's chunks 6.9 each — the plan's
            1.5–2 ms was the read's cost missed; a chunk bite through the hook is 14.4 ms; the
            room under 5 m of head is 1.76 L/s, ankle-deep in 3.9 h, dirt 17.5× that.
            - [x] **The scan reads a classified padded copy (M8c-2).** 0.56 ms a solid chunk against
                  2.5 per voxel, ~1 ms a room chunk against 7, through the streamer's fluid gate.
                  Still on the sim thread at arrival; the played world's open is the play check,
                  and a worker scan is the lever if the `M2c:` or `M8b:` lines show it biting.
            - [x] **The bite is a rescan (M8c-2).** Past 64 edits in a chunk between flushes the
                  tracker re-scans the chunk at the boundary instead of 32,768 hooks.
            - [ ] **The per-tick coefficient is 0.073 µs a group** (345 groups for 6,613 faces);
                  the played mine's ~4,584 chunks at ~40 groups each would be ~13 ms a tick Release
                  if every chunk were walked. The drain skips a full chunk before its groups; a
                  chunk under dry samples still pays its groups' table reads. Read `seep pass mean`
                  on the played world before caching anything.
      - [x] **M8c-2 — the draw. Built 2026-09-11**; see M8-PLAN's "M8c-2 — built". `SeepDrain`
            at the boundary after the portal drain: litres out of the basin through `Seep`
            (`Remove` counted apart), into Tier B lowest-face-first, faces below their own table
            only; a full chunk skipped until its faces change or 16 passes pass; `seeped` on both
            identities; `--seepscale=` (0.06, zero is off — no index, no drain); the `seep` profile
            line with the awake count at seeping boundaries, min/mean/max. **The dug rule is on the
            inside's chunk (`SeepIndex.IsDug`), because the portal rule wet a cave beside the
            first shaft bitten.**
            - [ ] **Play check, on the played world (the awake count is the water-bodies decision):**
                  1. Stand where the big lake is and dig a `hand_pick` tunnel into its bank, well
                     below the water, without breaking through. 2. Watch the tunnel floor for a
                     few minutes of world time; at the shipped scale a small tunnel takes a litre
                     every few minutes, so `--seepscale=10` passed to `start.cmd` is the arm that
                     shows it inside a play. 3. Read the `M8b:` line's `seeped … through N face(s)`,
                     `seeping / full on the last pass`, `seep pass worst`, and `awake at seeping
                     boundaries min/mean/max`. 4. `Q` a chunk out under the lake and read the same
                     terms, plus `displaced 0` and both identities exact. **The sentence wanted
                     back: whether the water arriving reads as the mine getting wet or as a tap,
                     and whether the awake mean at seeping boundaries is tens or thousands.**
                     **Half answered 2026-09-11 (scale ×10, 33,113 L seeped through 15,352 faces,
                     identities exact): it reads as a tap, and that is kept — hard rock lets water
                     in through joints, not through the wall. The awake mean read 419 while only
                     tunnelling and ~49,000 after a `Q` bite let the lake in through portals, so
                     the bodies number is the flood's, not the drip's: it still wants one session
                     of tunnelling under the lake without breaking through.** **Then Matthew asked
                     for the sweat after all, for wading: `--seepmode=sweat` is built (each group
                     hands litres round its faces; spring stays the default). The tunnel-only
                     session is now an A/B: once under each mode, `awake at seeping boundaries`
                     mean off the `M8b:` line for both, and which reads like a mine.** **Answered
                     on the bench 2026-09-11 (`--seepawake`), because the game's count reads the
                     whole world's awake cells and the played world's flood swamps the drip: at the
                     shipped scale a small room is quiet 97% of ticks, a 10 × 10 × 3 m room keeps
                     141 awake under a spring and 18 under sweat; at ×10 one to two thousand. A
                     drip does not force water bodies; bodies stay after M8d. What is still play's
                     is the look A/B.**
            - [ ] A delivery is an inflow to the settle (`Add` notes it), so a drip-fed chunk's
                  puddle levels by diffusion and never by the solve. Revisit trigger: the play
                  reading the puddle as syrup; lever: water bodies, where a delivery is a number.
            - [ ] **The `awake at seeping boundaries` term is the world's count, not the drip's.**
                  Read it against a `--seepscale=0` control on the same world or not at all; the
                  clean number is `--seepawake`. Lever if it is ever wanted in play: count the cells
                  the drain's own deliveries woke, which the sim would have to attribute.
            - [ ] The rate is measured against the lake, not the head difference to the mine's
                  own water, so a mine seeps at the full rate until it is full and then stops,
                  rather than slowing as it fills. Bodies give the honest difference for free.
      - [x] **M8c-3 — `Saturation.HeadMetresAt(voxel, table)`. Built 2026-09-11**: head below
            the column's table in metres or zero, the same quantity the seep coefficient
            multiplies (asserted), unread until M9.
      - [ ] Deliberately out: lateral groundwater (a tunnel under a divide from a lake gets
            nothing — the revisit trigger is that play), seepage from rain with no lake, natural
            caves seeping, the damp-wall look (M8d), solubility (its own pass after M9).

- [ ] **Water bodies — resting water as one number over a void (Matthew, 2026-09-10, "not
      yet").** [docs/WATER-BODIES-NOTES.md](docs/WATER-BODIES-NOTES.md). DESIGN's *stable cells
      merge into a Tier A body*, never built; the mine gets a Tier A body and the seams
      (`SurfaceHandoff`, `PortalDrain`, `PortalIndex`, `VolumeCurve`) already exist. Buys memory,
      the autosave copy, the restore, the surface mesh and the settle at rest, and turns a hole
      under a mine lake into a portal drain rather than a woken front. **Does not make the tick
      faster** — sleeping cells cost nothing already — and the roof-open drain is cheaper, not
      more accurate, unless the mouth draws by head. Six decisions in the notes; belongs in M8c's
      planning pass, opening with the void-curve derivation's cost (32,768 voxels a chunk bite,
      ~1.3 million for the played mine). **Pressure is a body quantity** — head below the level —
      so the underwater ladder (oxygen → rebreather → entangled lungs, M10-NOTES *Being
      underwater*, survival-mode trigger) is on this item's path too.

### M9 — v0.5, the game exists

**Planned 2026-09-11 — [docs/M9-PLAN.md](docs/M9-PLAN.md), split M9a–M9d, twelve decisions all
taken as recommended the same day (*"As planned"*). M9a is next, in its own session.** Two facts from the code open it: there is no bedrock (the
anchor is the edge of the analysed set, M4a's *unloaded is solid* extended to *and holds*), and
DESIGN's literal distance rule has no arch — a 2 × 2 bite in stone would open a one-metre shaft to
the surface, where the plan's budget rule sheds a 20-voxel plug and holds.

- [x] **The decisions** — all twelve taken 2026-09-11:
      - [x] 1. A budget with an arch: sideways spends the material's share, resting refunds a world
            constant (`RestRefundVoxels` 30, estimate), hanging costs double; strictly negative cycles
            make the fixpoint unique. `lamp.json` 4 → 8 rides on it.
      - [x] 2. Per voxel, one byte, only where non-trivial (32 KiB an analysed solid chunk; the cell
            rule the named fallback).
      - [x] 3. Analysed set = dug chunks + six face neighbours; a natural cave is stable until
            touched; exactness in the visible box is *not* the margin's — the failure is a later fall.
      - [x] 4. From-scratch derive per chunk on a worker, neighbours' faces captured, re-derive
            whoever's input changed; coalesced at `--supportcadence=` (250 ms, estimate).
      - [x] 5. `supportUnderHead` — head metres → distance, piecewise linear, per material; stone
            `[[2,24],[10,12]]`, dirt `[[1,4],[3,1]]` — **estimates, Matthew's**; `--supportscale=`,
            `--saturation=0`.
      - [x] 6. Loose voxels dropped straight down, instantly, into one `overburden:rubble`; not
            replaceable; voxel count conserved, Σ(Y) decreases.
      - [x] 7. Nothing persisted; re-derived at open, delays re-rolled, no fall until the
            neighbourhood has derived.
      - [x] 8. Delay seeded by `Hash64(seed, chunk, world ms)`, world seconds, 2–60 s (estimates,
            **Matthew's**), shorter with the deficit; `--collapseafter=`, `--collapse=0`.
      - [x] 9. Water under a fall collected and re-poured into the cavity at the boundary, never
            destroyed; **and a fill refuses a voxel holding water — Matthew's.**
      - [x] 10. ~~Health at the minimum: damage per voxel on the body, buried past 4 m is death, death
            is respawn with the inventory kept; not persisted. DESIGN's M11 row → *the rest of health*.~~
            **Sent back to M11 on 2026-09-13** — *"for debugging I dont need it really"*; DESIGN's rows
            amended, a fall buries the body and c-1's unstick frees it or says it is buried.
      - [x] 11. Warning signs ship with the fall: `COLOR.b` strain in the merge key, dust from the
            underside; sound waits.
      - [x] 12. Split M9a field / M9b wiring / M9c-1 fall alone / M9c-2 body / M9d keystone.
- [x] **M9a — the field.** Built 2026-09-12 — read M9-PLAN's "M9a — the planning pass" and "M9a —
      built". `Overburden.Core/Structure/` (`SupportRule`, `ChunkSupport`, `SupportCostTable`,
      `SupportFaces`, `SupportDerive`, `SupportRegion`), `MaterialTable.SupportDistanceAt`, the
      loader's cap and curve, `supportUnderHead` in the shipped JSON, `--support` on the bench.
      **First number: 0.8–1.1 ms a searched derive, 0.2 ms a gather, Release with tiering off**
      (the plan said 1–3; the cell rule's 4 ms trigger is far). Three departures the build made:
      `K = 120` (the byte), a path dies at `−K` and unreached is a state (the floor as a source held
      a mid-air chunk's top and made a second fixpoint), the join starts from below (33 rounds → 2).
      Suite 1,296 → 1,319; eleven recipes, ten caught, one identity.
      - [ ] **Lever, named and not built:** the gather is a fifth of a derive and is
            `PaddedChunk.CopyFrom` through `VoxelChunk.Get` — M5c-1's lever for light, unbuilt there too.
      - [x] **Decision 3's cost in a number, and Matthew's answer (2026-09-12):** the bench's
            natural-cave row sheds nothing on the first bite because the cave's ceiling is in the
            chunk beyond the shell, and the second bite may bring it all down. *"Isn't an issue,
            feel free to make dangerous any old save"* — so the shell stays as built, and **the
            played world gets no grace at open**: a 264 × 304 m chunk-bitten void will be at risk
            wherever the field says so the moment M9b derives it, and M9c will drop it. No
            migration, no per-world switch. The one thing that still stands is decision 7's
            barrier — nothing falls until a chunk and its six neighbours have derived.
- [x] **M9b — the wiring.** Planned and built 2026-09-12 — read M9-PLAN's "M9b — the planning
      pass" and "M9b — built". The derive is a detached job (`SupportDeriveJob`) pumped off
      `SupportRegion`'s queue by `ChunkPipeline.Support`, at most 32 a frame; coalescing is the
      in-flight mark; the tint reads settled fields only (a chunk and its six at rest) and remeshes
      non-urgently when the at-risk set moved; strain is one bit in the key on `UV2.y`;
      `--support=0`, `--supporttint=0`, `--bitechunks=<n>`, `--bitedepth=<n>` (3), `--inbite`; the
      `M9:` line and the `support` profile line. **Oracle met: `at risk 20 voxel(s) in 4 settled
      chunk(s)` on the seed world, r6 control unchanged to the digit, the plug tinted on screen and
      measured by pixel.** Suite 1,319 → 1,332; 18 recipes, 16 caught, both misses named.
      - [ ] **Owed: the played world's open on a world with a shell.** The chunk-bitten void is
            gone; `default` has 220 stored chunks and one in the spawn's box. The `support` line
            and `M9: at open` are ready for the first world dug big enough.
      - [ ] Decision 2's deferred timer: `--supportcadence=` only if derives/s on the `support`
            line scales with swing rate until worker time shows on `sources`.
      - [ ] Decision 5's cost: the edit hook is per voxel, three set lookups a call after the
            first; the lever if a bite hitches is the per-chunk dirty list `SetVoxel` keeps.
      - [ ] The parallel join re-derives more than the loop (86–209 against 34 for a 2 × 2) —
            worker time the player never waits for; a lever if the `support` line's `requeued
            stale` ever matters is to hold a chunk's submission until none of its six is in flight.
      - [ ] The tint's look — a graded tint (deficit / K per face) is one line in the mesher, a
            screenshot's call, and a quad cost; judged at the first play.
- [x] **M9c-1 — the fall.** Alone. **Planned 2026-09-13 — eleven decisions in M9-PLAN's "M9c-1 —
      the planning pass", all taken as recommended the same day (*"as planned"*); split c-1a / c-1b
      the same evening on Matthew's *"smaller pieces"*; c-1a built 2026-09-13, c-1b built
      2026-09-14 — the oracle reads as planned and the bench drops every fixture with both
      oracles holding; unplayed.** Rubble, the timer, the re-pour, the
      cascade, `--collapse=`, `--collapseafter=`, the bench's `--collapse` arm. The pass's spine: a
      fall is a *column run* followed downward across faces into settled chunks (a run straddling a
      horizontal face would otherwise turn to rubble in place and fire as a no-op every boundary);
      the gate is `IsSettled` now plus the world, because `SettledField` is the last settle and not a
      promise; the timer is a sweep at the boundary and the cascade is the re-arm from the new
      deficit; the re-pour goes bottom-up from above the new rubble; `lifted` / `returned` on Tier B
      alone, the identity untouched, a fire waiting for fluid-ready chunks so `displaced 0` is an
      oracle. **The unstick is here** (a body in rubble with no health is stuck, not unhurt) —
      taken 2026-09-13, with damage and death sent on to M11. Oracle: `--bitechunks=2 --collapseafter=1,2` reads
      `fell 20 voxel(s)` then `at risk 0`; r6 control to the digit.
      - [x] **c-1a — Core, built 2026-09-13.** `Collapse.TryFall`, `CollapseScheduler` and
            `CollapseWindow` in `Core/Structure/`; `SupportRegion.TryReadSettled` / `SettledFields`;
            `ICollapseWater` and `FluidSimulation.Collapse.cs` (`Lift`, `Return`, `DisplaceLifted`,
            `IsReady`; `lifted` / `returned` on `FluidPublished`); `rubble.json` and its colour;
            27 tests, 22 recipes, 21 caught and one an identity. Read M9-PLAN's "M9c-1a — built".
      - [x] **c-1b — the wire, built 2026-09-14 as briefed.** Read M9-PLAN's "M9c-1b — built":
            the oracle `--profile --speed=0 --bitechunks=2 --collapseafter=1,2 --measure=20` reads
            four `M9: fell` lines one boundary apart, `20 voxel(s) fallen`, `at risk 0`, the r6
            control to the digit, `--nowater` the same fall with no water terms; the bench's
            `--support --collapse` drops all seven fixtures with both oracles holding and floods
            fixture 2 (312 L lifted == 312 L returned, 0 displaced, one tick to rest). **Play
            checks, owed**: `Q` a 2 × 2 out under stone on the played world and wait the window
            out (the plug's deficit arms near the 60 s end — `--collapseafter=` shortens it);
            stand under it walking for the unstick line; bite under the played lake and read
            `displaced 0` and `lifted == returned` on the `M8b:` line. The brief as built follows.
            In order: `--collapse=<0|1>` and
            `--collapseafter=<min>,<max>` parsed beside `--support`; the scheduler built in
            `SetUpSupport` over `_pipeline` as the `IVoxelEditor`, `_fluid` as the water (null under
            `--nowater`), `_settings.Seed`, `_clock.ElapsedMillis`; `SweepCollapse()` in
            `UpdateFluid`'s `begin` block after `PourPending()` and before `_field.Sync`, and from
            `UpdateSupport` every frame when `_fluid is null`; one `M9: fell …` line per
            `CollapseFire` (chunk, voxels, columns, deepest drop, litres lifted / returned, `cascade`
            when flagged, the fall's ms); the `M9:` line and the `support` profile line gaining
            `armed / due / waiting (unsettled N, fluid-pending N)`, `fired`, `voxels fallen` with the
            mean drop, `stale skipped`, `lifted / returned L`, `freed / buried`; `M8b:`'s identity
            line carrying `lifted` and `returned` off `Published`; the unstick after any fall whose
            `Written` chunks overlap the body — `VoxelCollider.OverlapsSolid` on `_player.State.Box`,
            `PlayerSpawn.TryUnstick`, `_player.Teleport(freed.FeetCentre)`, `M9: the player was
            freed N m up` or `M9: the player is buried`; the bench's `--collapse` arm on `--support`
            (a `DictionaryEditor` telling the region and the automaton, `Σ(Y)` before and after, the
            picture after beside the picture before, fixture 2 flooded through `VoxelFluidTerrain`);
            the headless oracle `--bitechunks=2 --collapseafter=1,2` → `fell 20 voxel(s)` over four
            boundaries, `at risk 0`, the r6 control to the digit, `--nowater` the same fall with no
            water lines; then M9-PLAN's built section extended, its decision-3 cascade paragraph
            amended (a stone or dirt wedge under anchored walls is one fall; the cascade is the
            chunk above joining the analysed set), CLAUDE.md's pick-up paragraph.
      - [x] **First play, 2026-09-14 (steel pick, a fresh `default`, ~25 min of world time):** six
            falls, 15 voxels, in two tunnelled chunks — three single voxels under a pond at t 405 /
            415 / 428 s (drops 3, 1, 1, the last two `cascade`), each lifting and returning one
            cell (16 L), then 1 / 5 / 6 voxels in a second chunk at t 1134 / 1396 / 1533 s with
            drops to 7. `lifted 47 L == returned 47 L`, `displaced 0`, both identities exact, `0
            stale skipped`, 3,382 sweeps at 0.028 ms mean, worst 3.9 ms (the first fall). **The
            wet oracle passed in play before M9d asked for it.** The unstick is still unexercised
            (`0 freed / 0 buried` — nothing landed on him).
      - [x] **The delay's jitter is too narrow — Matthew, after the play — widened at M9d (2026-09-15, decision 7: an exponential with the deficit's delay as its mean; the paragraph below is the note as it stood):** *"I want it to be
            unpredictable like, like you know if you have left too much overhang"*. Today the draw
            is `lerp(max, min, deficit / K) × U(0.5, 1.5)`, so a thin lip always goes in about a
            minute and the 6-voxel shelf read as a timer (it sat on six `M9:` lines before it went).
            **The lever is the draw's width, not the deficit rule**: keep the lean (more overhang
            falls sooner on average) and widen to `U(0.1, 2.5)` or an exponential with the deficit's
            delay as its mean, so the same lip can go in 5 s or 150 s. One constant in
            `CollapseWindow.DelaySeconds`; `--collapseafter=` still sets the ends; re-run the
            seed-world oracle so the four falls still land inside `--measure=20` (a wider draw at
            `1,2` may need a longer window or `--clockscale`). **Do it on his word after the next
            play** — he has not been hit yet and is mining again.
      - [ ] **Lever, named and not built:** `FluidField.Invalidate` clones 4 KiB a voxel, so a fall
            under a lake churns tens of MiB — a per-chunk invalidation on a bulk edit, M8c-1's seep
            lever, when a fall under the played lake hitches. **c-1b's number for it**: a plug's
            ten writes cost the sweep 3–13 ms Debug with the water's hooks on and 0.5–0.9 ms under
            `--nowater`, with no water anywhere near — the hook chain is the whole bill, and a
            4 × 4 room's biggest stage (11,703 voxels) is ~18 ms in one boundary at the bite's rate.
- [ ] **M9c-2 — dust.** Decision 11's other half, optional, judged after M9d's play (the tint may be
      enough). Health, damage, death and the HUD bar are M11's since 2026-09-13; the unstick is c-1's.
- [x] **M9d — the keystone and the play. Planned 2026-09-15, all ten decisions as recommended and
      built the same day; the play is Matthew's.** Read M9-PLAN's "M9d — the planning pass" and
      "M9d — built". The table-move trigger (readings compared at each fluid boundary, memoised
      per sample, requeue iff the rebuilt cost table's bytes differ — `table N (S sweep(s), C
      compared, R rebuilt, U unchanged)` on the `M9:` line, `table` on the `boundary` line), a
      timer that only shortens (`N armed (M shortened)`), the exponential jitter, the fill's
      refusal (`Wet` on the `M6c:` and `building` lines, read from a published per-panel bitset —
      never the grid), `--saturation=0`, `--tablemove=0`, `--fillbasinafter=<s>`. Headless: the
      keystone reads `at risk 20` dry → `table 20` → `at risk 18,718 in 7` → four ~6,200-voxel
      falls, `lifted 30 == returned 30`, `displaced 0`, `at risk 0`; the three controls hold.
      - [ ] **The play — four questions as recipes, one sentence wanted back on each.** On
            `default`, `start.cmd` with `--entanglechunk --nogate` (a 16 m room on the cube arm is 256
            clicks; the excavator costs 1.6 MJ a chunk since 2026-09-19, two a pack — M10d's pass, fact 5). (1) *Settling*: `Q`, four chunk bites in a 2 × 2, 24 m down in stone; walk
            back and wait the minute out, or `--collapseafter=2,10`; one `M9: fell 20` and the roof
            holds — did it read as the roof settling or as a glitch? (2) *Attributable*:
            `--collapse=0`, the same room, look up — could you say where it would go before it
            went? (3) *The soil patch*: one chunk bite whose top face is in the surface soil, from
            beside it; ~1,000 voxels of dirt fall — did dirt falling and stone holding read as two
            materials? (4) *The water brought it down*: the same 2 × 2 under the pond, then once
            more with `--saturation=0`; `at risk` twenty against thousands, `lifted == returned`,
            `displaced 0` — did the wet roof going and the dry one holding read as the lake's
            doing? Then two the questions do not name: dig a room under a dry hollow, stand in the
            hollow and `R` ten thousand litres a few times (the hand-off raises the basin), read
            `table` climbing on the `M9:` line and the tint arriving on the roof untouched; and `R`
            into a room, a brush into the pool — `wet 64` on the `M6c:` line, the water untouched,
            `displaced 0` on the next `M8b:` line. **Answered by the play 2026-09-19, the other
            way**: *"it makes it impossible to build where there is even a slight amount of
            water"* — the refusal stands at the click and the boundary fills displacing; see the
            item below.
      - [x] **The chunk fill is deferred to the boundary — Matthew, after the first play, on the
            log's number (the fifth chunk fill into a chunk mid-flood: 3,516 wet refused, 18,955 L
            still displaced): *"defer the chunk fill to the boundary"*.** `DeferChunkFills` on the
            controller, a queue of clicks run after the pour behind a fresh `Publish`; the brush
            stays instant. `--nowater` fills at the click. Built 2026-09-15.
      - [x] **A fill into water displaces it — Matthew, 2026-09-19: *"absorb the water or
            displace it or remove it, your choice"*.** The click refuses wet as before and queues
            the shape; the boundary runs it with `FillWater` (M10a-2's machine rule on every
            shape: lift, write, return bottom-up over the box grown by a cell, the rest
            `displaced`), the chunk fill too. `RunPendingWetFills`, `--placeafter=`,
            `PlaceDisplacingTests` (+8), the `M6c:` bracket. Read M9-PLAN's amendment.
            - [ ] **The click's dry path can still displace by staleness**: the wire's run shows
                  `displaced 176 L` on the fluid line from the instant brush writing cells the
                  published copy had not seen wet (M9d's 64-cell bound). Now that the boundary
                  path exists, the lever is a brush that defers whole whenever its cell's chunk
                  is wet at all — one `HoldsWater` scan of the box before the write. Not built;
                  wants a play that notices the 176 L.
      - [ ] **Decision 3's edge is Matthew's after the play (*"not sure about 3 yet"*):** a fill
            under a Tier A plane is allowed — a lake is a body with no cells and its volume is a
            number terrain cannot reduce, so a pier deletes nothing. If a rock placed in a lake
            reads as wrong, the refusal is one read: `LakeCeiling.InLake` (M8d-5) beside
            `HoldsWater` in `ChunkPipeline`, a `Wet` for the lake's cells too.
      - [ ] **Lever, named and not built:** the sweep asks the world once per distinct sample and
            the dictionary behind the memo is most of its 7 µs a chunk (Debug); a per-column
            reading cache keyed by chunk column would halve the asks on a stacked mine. Trigger:
            `table` on the `boundary` line reading over a millisecond on the played world.
- [x] After the decisions: DESIGN's *Structural integrity* (bedrock → the analysed edge, the arch,
      *deferred*), MATERIALS-NOTES decision 1, `MaterialDefinition.SupportDistance`'s comment,
      SCALE's M9 paragraph — all amended 2026-09-11.
- [x] **`supportDistance` is already in the material JSON, unread since M1** — the plan reads it,
      per material, as the curve's dry point. Solubility stays its own pass after M9.

---

## Phase 3 — Depth


#### Deferred collapse — Matthew's proposal, 2026-09-01, and the leading candidate

Raised before M9 is planned: *"realtime collapse isn't necessarily required, like I'd be ok of a
cavern or cave or overhang of too much collapsed at a random time after becoming a possibility
like that way we can use it to damage your health so you always mine the overhangs etc."*

**This is a better mechanic than continuous collapse and not merely a cheaper one, and it should
be the starting point of M9's planning pass rather than a fallback in it.** Five reasons, and the
first is the one that actually matters:

- **A random delay decouples the collapse from the frame it became possible, which changes the
  failure mode of the whole subsystem.** M9's own line already says *incremental, local, budgeted,
  off-thread — never global*, and a budget that falls behind on a continuous solver produces
  **wrong physics**: rock that should have fallen and did not. Under a deferred model a late
  evaluation produces a **later collapse**, which is indistinguishable from the design. **The
  system is allowed to be slow**, and that is worth more than any constant factor.
- **It licenses coarse, lazy evaluation.** "Is this overhang at risk" is a regional judgement, so
  it can run on a coarse grid, on a timer, sampled — rather than per-voxel on every edit. The
  saving is in *promptness and resolution*, not in skipping the support analysis, and that is the
  honest way to state it.
- **A delay is what makes it a hazard rather than a puzzle.** Deterministic instant collapse
  teaches the player to avoid an exact configuration; a delayed one teaches them **not to be
  under it**. DESIGN says the mine is the antagonist, and an antagonist with an element of chance
  reads as alive where a solver reads as a machine.
- **It gives warning signs somewhere to live.** Under instant collapse a creak or a dust-fall has
  no window to occupy. Under this one there is a natural interval between *became possible* and
  *fell*, which is where the entire vocabulary of mining dread goes — and it is nearly free.
- **It pairs with damage, which is what Matthew wants it for**, and M9 already lists
  *damage/death from collapse*.

**The one real objection, and it is a design problem rather than a technical one: a purely random
collapse is not attributable.** For *"so you always mine the overhangs"* to be learned, the player
has to connect the damage to the thing they failed to do. Damage arriving from an unannounced
ceiling reads as arbitrary and teaches nothing. **So the warning signs are not polish — they are
what makes the mechanic legible**, and they should be planned with it rather than after it.

Three things to settle in M9's planning pass, all cheap now and expensive later:

- [ ] **Does the at-risk state persist across a quit?** M6 made this a real question. Either the
      timer is a stored field, or the at-risk set is recomputed at load and the delay re-rolled.
      **Recomputing is cheaper and probably also better** — it means a save cannot be used to
      freeze a ceiling indefinitely — but it needs saying out loud, because "my base collapsed the
      moment I logged back in" is the version nobody wants.
- [ ] **The randomness must be server-authoritative and seeded**, per non-negotiable 1: Core is
      the multiplayer seam, and a per-client roll means two players see different ceilings.
      Trivial to get right now, a protocol change later.
- [ ] **What the delay is a function of.** A flat random interval is the simplest and probably
      enough; making it shorter as support degrades is the obvious refinement and is what would
      let **saturation degrading support distance** — M9's recorded keystone interaction — express
      itself as *the ceiling starts creaking sooner* rather than as a step change.

#### A cosmetics pass, gated on headroom — Matthew, 2026-09-02

Raised while closing M7's play question 3: *"Nah like it's a cosmetic, we can introduce a cosmetics
pass if we find we haven't chewed all available CPU by the time we get to the end of just what we
like talked about in design."*

**This is a scheduling decision rather than a note about splashes, and it should be recorded as
one.** It gives every deferred cosmetic a single named home and a single named gate, instead of
each one being re-argued on its own merits at the moment it is noticed.

- [ ] **The gate is headroom at the end of the DESIGN roadmap**, not a milestone. Nothing here is
      scheduled against M8, M9 or M10; the pass exists only if there is budget left when the
      antagonist stack is finished. **That ordering is right and matches DESIGN**: the mine is the
      antagonist, and none of these items make it more so.
- [ ] **The resource named is CPU, and for the first item in the pass it is the wrong one — which
      makes the gate sharper rather than wrong.** Splashes are particles: M7c-2 measured the rain
      at **one draw call and 48,000 extra primitives**, with the sim-thread arms *lower* with rain
      on than off, because everything a drop does is in the process shader. **So the pass wants two
      budgets, checked separately** — CPU headroom for anything that thinks, GPU headroom and draw
      calls for anything that only draws. Several likely members are in the second category.
- [ ] **What is already deferred into it**, each with its own worklog entry to start from:
      - [ ] **Splashes** — cut at M7c-2, four findings on the shelf, and the play looked for them
            deliberately and did not mind their absence. The expensive knowledge is that a
            `texture()` call inside a particle `start()` silently yields particles that never
            appear: no log, no compile failure, and by eye identical to every other broken state.
      - [ ] **The 4 m shoreline**, if M8's Tier B has not already replaced it. Judged tolerable by
            the play; refining it before M8 would be building the thing M8 deletes.
- [ ] **Revisit trigger for the pass itself: any cosmetic deferred here being independently raised
      a second time.** One deferral is a judgement; two is a signal that it was not cosmetic.

#### The minimum drainable hole — Matthew's question, 2026-09-02, and it decides an M8 design

Asked while answering play question 5, and it is the sharpest thing the play has produced:
*"Unless I make a hole 4x4 it isn't going to drain into eventually?"*

**The answer is no, it will drain — but only because of a decision M8 has not made yet, and this
question is what makes that decision non-optional rather than a preference.**

**The 4 m grid is Tier A's analysis resolution, not water's physics resolution.** It is where
basins, volume→level curves and the surface sweep live. A drain is a Tier B event, and Tier B has
no reason whatsoever to run at 4 m — DESIGN specifies it as a CA over an explicit active-cell set
and **deliberately never states a cell size**.

So M8 has two shapes available and they differ by more than cost:

- **Re-analyse: rebuild the watershed from edited heights.** The seam DESIGN names is `SkyColumn`,
  which already exists and already persists. **This is the one that produces Matthew's failure.**
  The height field samples every 4 m, so a 0.75 m `hand_pick` bore is invisible to it — literally
  between samples — and the minimum drainable hole becomes 4 m across. **In a game whose starting
  tool leaves a 0.75 m bore and whose player is 0.5 m wide, that is absurd**, and it would be
  discovered by a player long before it was discovered by a test.
- **Breach: Tier B detects the opening at voxel resolution and pulls volume out of the Tier A
  body.** Tier A never learns about the hole at all; it simply has less water, and its level is
  re-solved from the volume it has left. **`SkyColumn` is per voxel column (32×32 per chunk
  column, so 0.25 m in x and z), and `IVoxelWorld` is per voxel** — the fine-resolution inputs
  already exist and are already persisted.

**The second is right, and this project has already solved the identical problem once.** M5b:

> A 1 m light cell holds 64 voxels and `hand_pick`'s bore is 0.75 m — **the tunnels this game is
> about are narrower than one cell.** So opacity is a property of the cell **face**, not the cell:
> light crosses iff some voxel pair across the shared 4×4 boundary is open on both sides.

The same sentence with "water" for "light" is the M8 rule: **a basin loses water through a face if
any voxel pair across that face is open, not if the 4 m cell is.** And the same failure mode
applies at both ends — "any open voxel drains the lake" empties a basin through a crack, "the whole
cell must be open" means a pick cannot drain anything.

- [ ] **Settle this in M8's planning pass, and settle it first**, because the two shapes imply
      different data structures rather than different constants.
- [ ] **The check has a natural home already: the rain occlusion field is built from edited sky
      heights at 0.5 m and a 0.75 m bore registers on it.** M7c-2 built that and measured it —
      whatever detects "daylight now reaches here" is close kin to whatever detects "water now
      reaches here", and `ChunkPipeline.SkyHeightChanges` is already the dig trigger.
- [ ] **Recorded consequence for M7:** none. Nothing drains at M7 at any size, so this cannot be
      half-shipped here. It is only a trap if M8 reaches for the re-analysis because `SkyColumn`
      was named as the seam and the 4 m grid was already sitting there.

#### Mountains and rivers — Matthew's ask, 2026-09-02, and lakes are already done

*"Eventually I'm going to ask for mountains, rivers and lakes like."* Recorded when it was said
rather than when it is planned, because two of the three already have a home and the third has a
trap in it.

- **Lakes are done.** M7's Tier A is exactly this: a basin, a solved level, and a surface. What is
  missing is not lakes but *deep* ones — the spawn's terrain is gently terraced, so this world's
  basins are puddles. **That is a worldgen question, which makes it the same question as
  mountains.**
- **Mountains are worldgen and are the cheap one.** `SurfaceHeightAt` is analytic and seeded, and
  three systems already read it — skylight seeding, the watershed's height field, and the rain
  occlusion's fallback. **So taller terrain costs no new machinery.** What it does cost is stated
  rather than assumed: more vertical chunks per column (vertical radius is the cheap axis, ~2 KB of
  voxels against ~46 KiB of mesh per drawn chunk, so this is affordable), and **deeper basins,
  which is already the recorded revisit trigger on `rainfallScale`** — a lake ten times the volume
  is ten times the wait at the same scale. The two asks land on each other.
- **Rivers are the trap, and they are M8 or later.** Overflow between basins is arithmetic at M7;
  a *visible* stream is moving water, which is Tier B. **And a river is the worst case for Tier B's
  sleep rules by construction** — DESIGN says *"sleeping is load-bearing, not an optimisation"*,
  and a river is water that never settles, so a naive CA river is a permanently awake active set
  the length of the map.
      - **The likely answer is that a river is not Tier B at all**, but an analytic channel found
        at worldgen — a chain of basins with a known flow between them, drawn as a Tier A body with
        a current, with Tier B used only where the player *breaches* it. That is the same shape as
        every other win in this project: the cheap version is also the more accurate one, because a
        river's course is a property of the terrain rather than an emergent result of simulating
        water on it. **It is a guess, labelled as one, and it is where a planning pass should
        start rather than what it should conclude.**
      - **Erosion stays out** regardless. The terrain is a pure function of the seed and
        non-negotiably so, so a river carves its valley *at generation*, never over time.

**None of this is scheduled.** M8 is water getting in; M9 is collapse. This is a note so that when
the ask arrives, the answers that already exist are not re-derived.

### M10 — Fighting back

**Re-planned the same evening as the ladder — [docs/M10-LADDER-PLAN.md](docs/M10-LADDER-PLAN.md),
Matthew's brief (*"the first tech we make will be the last tech in the game"* — hand → steam → DC
→ AC → quantum, with crafting from parts made earlier), twenty-three decisions with
recommendations, ten parts M10a–M10j in four groups; the morning's [docs/M10-PLAN.md](docs/M10-PLAN.md)
is its first rung with eight decisions amended.** The ladder's one idea: connection is membership
and movement is a ledger transfer at the boundary — a wire, shaft or pipe is evidence that two rows
share a pool, never a thing that carries anything, so there is no solver and the DC tier's short
run and the AC tier's unlimited one are one code path with one number different. Five things
taken during the pass in his words: iron under the water table; the first age stone and wood;
trees found and planted with seeds from fire and growth on a worker after the tick; channel 0–15,
outputs cycling on a per-channel cursor, every input firing every tick; the channel set by
right-clicking with a computer; machines below quantum with faces. His hose-as-FIFO is returned
as decision 2 with the cheaper arm recommended (a presentation fill and a live leak at a cut).
**And, on reading it: pipes have no run bound at any tier — the head is the fluid tier's bound**
(*"a fair bit more forgiving distance wise … you might need more to go deeper"*), decision 3 amended.

- [x] **The twenty-three decisions — all taken as recommended 2026-09-15 (night): *"As
      recommended"*.** Matthew's were 1, 2, 3's numbers, 6, 8, 9, 11's `E` split, 15–16, 18, 19,
      20, 23. Each part wants its own pass against the code before it.
- [ ] **M10a — planned 2026-09-15 (night), the pass at the end of M10-LADDER-PLAN: six facts from
      the code, twelve decisions, three sessions.** The facts that moved it: an autosave is *three*
      store calls (header, basins, batch); `Dispose` saves too; the hooks fire inside `Complete` and
      the two plans put writes in two of them; a fall and a swing write the same thing into a body,
      so the rubble clause cannot tell them apart — `Carve.Take` can (`Lost == Carved`); the table
      is a level, so a pump in a pit beside a lake would drink it through the rock — `InLake` is the
      question; `BasinAtSample` folds *off the window* into `Outlet`. **`--pump`'s delivered rate is
      the first number** — every pump cap in the ladder is fiction until it.
      - [x] **M10a-1 — the store slice, built 2026-09-15 (night), alone.** `WorldBatch` with the
            optional session (machines, basins, header) in one transaction on both stores,
            `SaveAll(session)`, `ReadMachines`, schema 4, `MachineRowBlob` v1, `PortDefinition` /
            `MachineRow` / `MachineSet` as rows only, `Main`'s autosave and quit on one call, the
            `M10:` lines. The first test is in the contract suite and both stores pass it; suite
            1,412 → 1,469; 15 recipes, 15 caught (one fixture fixed — the appended-blob recipe
            tripped the frame's guard first); r6 control to the digit; the played world's copy
            opens at schema 3 and gains the table. Read "M10a-1 — built" in M10-LADDER-PLAN for
            the four departures (`Basins` nullable for `--nowater`; `WriteSession` as the quit's
            fallback when the tick still owns the grid; the quit's order; blob equality).
      - [x] **M10a-2 — the entity, built 2026-09-15 (night).** The loader's `machine` block and
            `unit` with every refusal naming the file, the relay pair at hardness 3.0, `Place.Machine`
            and `MachineDisplacing`, `IMachineBodies` on `Carve`, the hooks that queue and the drain
            that writes, `P`, the hotbar's `× N`, `MACHINE`, `--abort=` — and `--standfor=` and
            `--material=` so the crash arm can be reached (`--place` aims level while moving).
            Suite 1,469 → 1,519; 17 recipes, 17 caught (one identity fixture); r6 control to the
            digit; the orphan direction seen headless (128 voxels cleared at the reopen). Read
            "M10a-2 — built" in M10-LADDER-PLAN. **Unplayed**: the look of the pair, `MACHINE` on the
            status line, a pump placed into a flooded room by hand are M10d's.
      - [x] **M10a-3 — the pump, built 2026-09-16 (small hours).** `Pump`/`Emit` twins on both
            tiers and in both identities, `HeldIn`, `TryBasinAtSample`, `MachineTick` in its
            `machines` bracket (every row every boundary: ≤ 8 `IsLoaded`, ≤ 8 `HeldIn`, the 26
            cells nearest first, or the lake through `InLake`; room first, then exactly that moved;
            whole litres across a tier; the owed counter; the channel's cursor in memory), the
            unloaded-outlet rule on the *mouth*, `--pump=<L/s>` (a rate override, the content's 100)
            and `--pumpoutlet=<m>` (24 loaded; **96 unloaded** — the loaded box is r6 plus the
            margin from the viewer, and the shaft stands at the pit), the `M10:` line's `tick:`,
            the `machines` and `machtick` profile blocks, `--pump` on the bench, the lifecycle
            with quits. Suite 1,519 → 1,551; 19 recipes, 19 caught (one identity fixture added
            before the battery — the whole-litre floor); r6 control to the digit. **The first
            number: the relay delivers its nominal 100 L/s on a flooded chamber at either radius,
            3.2 ns a voxel to read the box, 500 wet pumps 3 ms a boundary, 500 dry 0.02 ms.** Read
            "M10a-3 — built" in M10-LADDER-PLAN. **The sleep set stays a lever**, trigger unchanged.
- [x] **M10b — the computer: planned 2026-09-16 (small hours), the pass after M10a-3's built
      section in M10-LADDER-PLAN — six facts, nine decisions (the ID `overburden:computer` is
      Matthew's; the rest recommended), two sessions; both built 2026-09-16, unplayed.** The facts that moved it: a right-click
      with anything selected places it, so `placeable: false` comes forward for the first item
      that is not a block; the computer's aim is mining's (the hit voxel), not building's; the
      capture handshake is two calls on `FlyCam` because both input handlers are *unhandled*
      input; `SetPort` must move `Version` or the tick keeps the old ring; the shipped pump's
      `weight` port reads nothing and comes out; nothing declares stock, so the stock tab is
      M10g's and the quit oracle is a port write.
      - [x] **b-1 — Core and the headless seam, built 2026-09-16.** `placeable` through the
            loader, the table and `Place.Validate`; `overburden:computer` (his word) with its
            colour and jitter, `P` seeds one; `relay_pump.json` without `weight`;
            `MachineSet.SetPort` → `PortWrite` (clamped, counted, `Version` on a changed value);
            the weighted ring by the smooth interleave with the per-boundary skip and
            `OutletRoomReads`; `MachineRow.LastStall` / `LastMovedMillilitres`; the controller's
            `SelectedIsItem` route and `PanelRequest`; `--port=`; `port writes` and `computer
            opens / misses` on the `M10:` line. Suite 1,551 → 1,569; 14 recipes, 14 caught; r6
            control to the digit; the port write survives a quit headless. Read "M10b-1 — built".
      - [x] **b-2 — the panel, built 2026-09-16 (morning).** `Overburden.Game/Ui/MachinePanel.cs`:
            the material's label and cell, one `SpinBox` per declared port with its bounds beside
            it, every change through `SetPort` at once and printed, the status line from
            `LastStall` / `LastMovedMillilitres` (an outlet's says what its ports mean); opened on
            `PanelRequest` through `Main.OpenPanel`, the mouse released after the panel shows;
            closed by Esc (`Main`'s while a panel is open, every other key ignored), by a click
            outside (the camera's own capture, seen by `Main` next frame) or by the row leaving
            the set. **Two rules the pass did not have**: the click that takes the mouse back is
            swallowed by both verbs until it is released (`FlyCam.ClickLive`) — before this the
            re-capture click swung at whatever the crosshair was on, which for a panel is the
            machine it was open on — and flight is gated on capture, because a key typed into a
            field is a key `Input.IsKeyPressed` still reports. `--openpanel[=outlet]` opens on
            `--pump`'s body, `--panelset=<name>=<value>` types into the open box (the widget's wire,
            headless: `channel := 15: set to 15` for a typed 99, then `stalled: no outlet on
            channel 15`), and the panel prints its status line on every stall-kind change. Three
            screenshots taken; suite 1,569 unchanged (no Core change); r6 control to the digit.
            Read "M10b-2 — built".
      - [x] **The pick's wire to the set, found and fixed 2026-09-16 by M10c's pass**: the game's
            `MiningController` never passed the machine set to `Carve`, so a pick on a body read
            `Lost > Carved` and destroyed it without credit in every play since a-2. `Bodies` on
            the controller now. **Lever, not built**: the damage marks are read at the frame's
            drain, so a bite and a fill into one cell inside one frame count the fill as damage
            and destroy the remainder — the headless fixture does exactly that (`--place --mine
            --entangle`, `lost 128 carved 64`; **`--nogate` too from M10c-3**, or the bite takes
            nothing); a hook that ignores a write of the body's own
            material *back* into a damaged row's cell is one line, and the recipe is a chip and
            an Alt-fill on one frame.
      - [ ] **M10b's play check — on a scratch world, one sentence back on each** (M10d's play
            takes the pump end to end; this is the panel alone):
            1. `P`, select the computer (`COMPUTER (in hand)` on the status line), right-click the
               ground — nothing placed, nothing swung. Select the pump, place it, select the
               computer, right-click the body: the panel opens centred, the cursor on it, the
               camera still.
            2. Change the channel with the arrows and by typing; the log prints one `M10: panel —
               … channel := N: set to N` per change and the `M10:` line's `port writes` climbs.
               Type 99: the box shows 15.
            3. Esc with the field still editing: the field stops editing (Godot 4.4+'s LineEdit
               consumes the first Esc); Esc again closes the panel and the mouse is captured —
               **is one Esc or two what you expect?** If one, the panel handles Esc itself
               (`_UnhandledKeyInput` on the panel, marked handled, the camera's rule untouched).
            4. Open it again; click the ground outside the panel with the left button: the panel
               closes, the mouse is captured, and **the pump is not bitten** (`ClickLive`) — the
               `M6c:`/`M3:` lines show no swing on that click.
            5. With the panel open press 1–9, Q, B, V, T, F: nothing changes (the log prints no
               `M6c: brush`, `M4c:` or tier line); WASD does not fly the camera.
            6. Open the outlet: two rows, `weight 1 … 100`, the status line naming its channel.
            7. The look — is a dim panel over the world the right shape, or does it want to sit
               to one side of the crosshair? (The cursor appears at the screen centre when the
               mouse is released, which is why it is centred.)
            Levers named and not built: a HUD status line saying `PANEL OPEN` while one is
            (the hotbar's line still names the brush underneath); an outlet's own `received`
            counter on the row for its status line (the tick writes only the pump's; one field,
            reset per boundary over the outlets, and a `MachineTickTests` fixture); a port
            declared wider than 2^53 rounds in the box before `SetPort` sees it (no shipped port
            is near it).
- [ ] **M10c — power on the channel class: planned 2026-09-16 (evening), the pass after
      M10b-2's built section in M10-LADDER-PLAN — nine facts, eleven decisions (the pack's ID
      and numbers and the entangled tool's threshold are Matthew's; the rest recommended), three
      sessions.** The facts that moved it: `joulesPerLitre` is parsed and read by nothing; the kind
      blob is carried verbatim and clamped nowhere; the sky column already answers openness per
      0.25 m column; the overcast is a per-frame float and the forced one is the shader's; the
      relay is 200 panels, so its M10c/M10d arms need a debug charge; the pick never told the set
      what it took (fixed, `2b26540`) and the gate's wire is the same shape; there is no tool for
      the `power` block to sit on; header v4 is one more presence-byte section.
      - [x] **c-1 — the pool in Core, built 2026-09-16 (evening).** Suite 1,569 → 1,591;
            twenty-two break-recipes, nineteen caught first time and three after the fixture each named was added — a converter with the sky as its source and the channel as its sink (the converter's own shape rule made the first recipe an identity), the pack's own capacity bound (the tick caps before it calls, so only the object's test sees it), and a lake pump under a litre of joules (the floor changes the stall's name, not the water); r6 control to the digit; the game runs `PowerOff` and prints it
            until c-2. Read "M10c-1 — built". What was planned: the `converter` and `storage` kinds through the loader
            (`ratedWatts`, `efficiency` per-mille, `capacityJoules`, `carried`, `rechargeWatts`;
            `solar` served as a sink `via`), `StorageState` and the openness byte in the kind
            blob, the restore's clamp, `ISolarSky` and `OvercastByte` on the tick, the pool per
            channel — the pump's bill at `joulesPerLitre` (a millijoule a millilitre), `unpowered`
            after `no water`, the pack last and never a supply, write-back in origin order,
            `clamped` — the counters, the identity `Σ charge + pack == loaded + generated − drawn
            − clamped`, `PowerOff`. Tests at the pass's end.
      - [x] **c-2 — the wire and the oracles, built 2026-09-17 (small hours).** Every oracle the
            pass named read to the millijoule (150,000,000 and 585,600 mJ over 60 boundaries for ten
            panels clear and under the storm; `59 unpowered` with `--cell=0`; a-3's oracle unchanged
            with the cell at 4,999.0 MJ), the r6 control to the digit, the bench's powered row at
            3.03–3.11 ms against 3.00–3.01 free for 500 pumps and 500 cells over two runs. Read "M10c-2 — built". What was
            planned: `Main`'s sky adapter, the overcast byte at the
            boundary, `P` (a panel, a cell, a pack once) and `Alt+P` (fills every loaded cell and
            the pack, printed), `--pump`'s charged cell beside the outlet (`--cell=0` omits it →
            `stalled 60 unpowered`), `--solar=<n>`, `--nogate`'s tick half, the `M10:` `power:`
            term and the profile block, the panel's three lines, the bench's power row,
            `--weather=1` → 976 mJ a panel a boundary; the three JSON files and
            `ShippedContentTests`.
      - [x] **c-3 — the pack and the gate, built 2026-09-17.** Header v4 with the pack, the equip
            verb and the swap (`forfeited`, a term in the identity — `stowed` since 2026-09-18, the
            charge kept beside the count), the pack's panel on the
            computer's click on nothing, `tools/entangled_miner.json` through `ToolDefinition.Power`
            and off the T cycle, `Carve`'s budget with `Unpowered`, both controllers paying, the
            HUD's pack line, `--pack`, `--charge=`, `--openpanel=pack`, `--nogate`'s miner half.
            The oracle reads `removed 6, unpowered 57 of 64` (one air voxel in the spawn's cube —
            the pass's 58 assumed a solid one), the pack at 40 J, and 40 J after a reopen; the
            r6 control to the digit; 26 recipes, 26 caught. **Two things found on the way**: every
            `--place` profile since M10b-1 selected the computer and placed nothing (fixed —
            placeable first, an item seeded at one), and two header tests had passed on the wrong
            refusal since M7b-2 (fixed with one `TrailerBytes`). Read "M10c-3 — built".
      - [x] **Matthew's, before c-3** — both taken as recommended: `lead_acid_pack` 3.6 MJ / 1 kW
            ships, the entangled tool's threshold is 5.0 (it takes a body, which a hand pick could
            not; this reverses M3d's *never what the bite is hard enough to take*).
      - [ ] **M10c's play check — on a scratch world, one sentence back on each** (M10d's play
            runs the relay dry and watches the storm; this is the pack and the gate alone):
            1. `Q`, click the ground with nothing equipped — the status line reads `Q TAKES NOTHING
               — no pack`, the click swings and takes nothing, the log prints `M10: gate — swung
               entangled_miner with no pack`. *Does a swing that takes nothing read as a gate or as
               a bug?* **Played 2026-09-17 (evening), on `default`, not the scratch world**: swung
               once, took nothing, `P` pressed twice, the fill refused fourteen times, no
               right-click ever made with the pack in hand — *"how do I use my inventory?"* A
               locked door, not a bug; the HUD's instruction under `Q` was not followed. The same
               play read the pool off a pump: one panel 0.25 L a boundary, two panels double,
               *"it started pumping more :-{D"* (WORKLOG, that evening).
            2. `P`, pick the lead-acid pack on the number row (`PACK (in hand)`), right-click
               anywhere — the line gains `pack 0 mJ / 3,600.0 kJ (0.0%)`, the log `equipped … empty`.
               *Did the right-click with nothing under it read as equipping?*
            3. `Alt+P` — `pack 3,600.0 kJ / 3,600.0 kJ (100.0%)`. Hold `Q`'s click on a hillside and watch the number
               run down: 3.2 kJ a cube, ~1.9 min to empty, then the bite stops mid-cube (`the pack
               ran out mid-bite: removed N, unpowered M`). *Does a bite that stops partway read as
               running out, or as the tool breaking?* (Decision 6's rejected arm was the whole
               bite refused.) **Played 2026-09-18 at 6.4 kJ and 56 s**: *"seems ok but you don't
               get far … be a little generous"* — the stop read as running out, and the price went
               100 → 20 J a voxel; an hour later at 20 J with `Alt+P`'s cell on the channel,
               *"I might have appealed to your generosity too much … trouble fighting solar
               recharge"* — the recharge is what you fight, so 50 J and the pack's 1 kW → 250 W
               (decision 16 amended twice; the arms are there).
            4. With the pack empty, right-click at range with `Q` on and a brush in hand — refused
               whole, `M10: gate — the fill at range needs 1,280 J`. *Is all-or-nothing on the fill
               right, beside the bite's running-down?* **Answered 2026-09-20 — no: *"1 and 3 as
               recommended"*; the fill takes the bite's rule** (the last amendment of "M10c-3 —
               built" in M10-LADDER-PLAN): it lands what the charge buys and stops, the rest
               `PlaceResult.Unpowered`, the print reads `stopped short: the rest of the shape
               needs 1,280 J`, and a chunk fill on the excavator in two packs is two clicks.
            5. Place a vacuum cell (`P` seeds one), `Alt+P` — the pack on channel 0 recharges at
               125 J a boundary from it: 12.8 s a cube. Pick the computer, right-click the sky — the
               pack's panel; set the channel to 1 and watch the recharge stop. *One sentence on
               the panel opening on nothing.*
            6. `P` again (a second pack in the count), pick it, right-click — the swap: the HUD
               warned `the equipped one's charge is forfeited`, the log prints the joules
               forfeited. *Is a swap that throws a full pack away the right rule, or should the
               outgoing pack keep its charge as an item?* (The ladder's decision 15 says a count
               has no charge; this is the play that judges it.) **Played 2026-09-18** — the swap
               threw 2,650.6 kJ away and the verdict was *"keep its charge as an item please"*:
               built the same evening as `CarriedPacks` at header v5, the fullest carried pack
               equipped and the outgoing one stowed with its charge; decision 15 amended, the
               identity's term is `stowed`.
            7. Quit and reopen — `a pack equipped (header v4)`, the charge to the joule. Then `T`
               through the tiers: the entangled miner is not on the cycle, and the steel pick is
               still free. *Does `Q` costing while `T` does not read as the design?*
- [x] **A three-step undo — planned and built 2026-09-22; his ask, mid-play, after the quantum
      excavator: *"how hard to implement 3 step undo Claude? :-{D Just plan"*.**
      [docs/UNDO-PLAN.md](docs/UNDO-PLAN.md): six facts, ten decisions, all recommended, one
      session. **It is small because nothing derived has to be undone** — light, support, the
      surface, the mesh and the collider all repair themselves from the same `SetVoxel` — and
      because `Carve.Take` and `Place.Put` are the one gate each way, with the prior material
      already in hand. `IEditJournal` as a fourth collaborator beside `into` and `bodies`;
      three steps grouped per chunk at six bytes an entry (192 KiB a chunk bite); Ctrl+Z on the
      edge, run at the frame's drain; **the stack is cleared by a fall, by a chunk leaving and
      by a quit** — the two traps are gravity and unloading, and clearing is the honest answer
      at three steps; the inventory is reversed exactly and a step that cannot pay is refused
      whole; no energy refund; a restore into water goes through M9d's displacing path and the
      water itself is not undone; no redo. Oracle: a click of every shape, undone, leaves the
      world byte-identical. **Built the same evening** — read "Built" in UNDO-PLAN: the journal
      hangs off `ChunkPipeline.SetVoxel` and `MachineSet` rather than seventeen verb signatures
      (the previous material is already read for the early-out), a step is a frame rather than
      a click, and the battery caught the undo journalling its own writes — the press after an
      undo redid the edit. `--undo=<n>` / `--undoafter=`, the `M10:` line's `undo` term, suite
      1,735, ten recipes and ten caught, r6 to the digit.
      - [x] **Played the same evening — *"Ctrl z feels good, disallow if something falls yes"*.**
            Decision 4 is settled by play, and his *disallow* changed one thing: a press after a
            fall said `nothing to undo`, which is *silently empty*, so `FellSinceLastEdit` carries
            the reason until the next edit and the press reads `the roof came down since your last
            edit, and rubble cannot be put back`. One test, suite 1,735; the bind and the
            three-step depth confirmed with it.
- [ ] **M10d — the play**: the mine pumped dry by relay, the storm, the gate, the pillar verdict.
      - [x] **The pool world is retired as the enclosure's fixture, 2026-09-22** —
            `worlds/retired-pool-2026-09-22/` (127 machines, 7.09 ML in 1,467 rows, 6.27 ML of
            it below the seed surface, `enclosed 82,543 kept / 1,052 closed`, a 450 kL walled
            pool, pumps on pedestals, an excavated mine). His offer after the session; the
            measurement world for the enclosure at scale and the natural fixture for the *dry*
            recipe, since both halves of `held` are large in it. A copy with `--world=` only.
      **Planned 2026-09-22 (afternoon), while Matthew played — "M10d — the planning pass" at the
      end of [docs/M10-LADDER-PLAN.md](docs/M10-LADDER-PLAN.md): seven facts, nine decisions, all
      recommended, split d-1 / d-2 / d-3. Nothing is built for it but two instruments and one
      test; the seven recipes with the sentence wanted back on each are the pass's last section
      and replace the three play lists above (M10b's, M10c's, the tiers') as the one list.**
      - [x] **d-1 — the instruments, before the play — built 2026-09-22 (afternoon)**, read "M10d-1
            — built" in the ladder plan; the oracle is the keystone, not a-3 (its shaft straddles the
            rim). Suite 1,719. As planned: `held` on the `M8b:`
            line and the profile's `fluid` block gains `(Q L in R panel(s) below the seed
            surface)` — a panel whose chunk top is under the least seed height over its
            footprint, off the loaded sky columns, memoised per chunk column (decision 2; the
            "dry" oracle); three tests on the plug fixture — a stone column floor to roof reads
            `at risk 0`, one voxel short reads 20, a clay column reads 0 (decision 5 — `Rest` is
            material-independent by the uniqueness proof, stated as a cost); the M9d recipe's
            `--entanglechunk` gains `--nogate`; the r6 control to the digit.
      - [ ] **d-2 — the play**, Matthew's: the seven recipes in the pass. Outlets past a divide
            or into a walled tank (never the mouth alone — a treadmill); the storm with no
            cells, which his world is; the pillar under `--collapse=0`; M9d's four; the tiers
            and the hold; the panel by hand; the pack across a quit. Watch for origin-order
            draw starving later pumps under panels-only power (decision 4; the lever is the
            outlets' ring on the pumps, not built until seen).
      - [ ] **d-3 — the solve's number on the load test, after his session** (never beside a
            play): a copy of `retired-spread-2026-09-22` with `--profile --fluidnear=2
            --fluidjob=1 --speed=0 --measure=60`, the `fluidjob` line's `solve mean` and the
            `settle` line at a minute; then the three levers below priced in their own pass.
      - [ ] **The quantum tiers and the hold (2026-09-19, unplayed)**: `T` cycles hand → steel →
            quantum miner → quantum excavator (a fresh world starts on steel); on the miner a
            click is one 1 m cell dug or the brush placed at range, on the excavator one 8 m
            chunk dug or filled at range, Alt one voxel on any tier; hold Q on any tier and it
            runs — the miner eats the hill at 10/s until you let go or the pack does, the
            excavator a chunk every 0.25 s and a lead-acid pack lasts two of them. One sentence
            back on each: does the excavator's two-chunk pack read as *the battery is the gate*
            or as broken, and does the cycle's order read as a ladder.
      **Begun 2026-09-19, on `default`, unprompted** — *"Well the pumps seem to work well"*, three
      screenshots: a wall of relay outlets on a lake's bank with the pumped water cascading off
      its end into the lake under rain, and a wet stair shaft below. Read off the two logs
      (2026-09-19, 10:53–11:26): 250 machines — 16 pumps, 20 outlets, 201 panels, 13 cells
      (65 GJ, `Alt+P`'s) — 8,161 L pumped in the first half hour and 4,356 L in the two minutes
      of rain after, `pumped == emitted` across the tiers exact on every line, 101 L to an outlet
      the box had not loaded (credited to Tier A, the unloaded rule in play), 15,017 `no water`
      stalls against 311 moves (the pumps wait on what arrives — the mine's 17.7 ML is elsewhere
      in the void), the machine tick 1.5 ms mean / 8.7 max for sixteen pumps, 0 boundaries
      waited under a 1.14 M-cell tick at 327 ms worker wall. 201 panels made 46 MJ in that half
      hour against the pumps' 8.2 MJ — 37.7 MJ `clamped` with every cell full — so the panels
      alone would have run them. The storm half is still to be watched for the dimming.
      **An hour later, the loop** — *"if you 'load up the system' too much you end up flooding
      your mineshaft"*, a screenshot of a wet tunnel: the same session read `pumped 60,379 L`,
      `handed 60,304 L to Tier A` and **`drawn 58,151 L`** — the outlets pour onto the bank of the
      lake that drains through the shaft's portals into the mine the pumps stand in, so 96% of
      what went up came back down, and once the lake stood over the outlets' mouths they stalled
      `no room` (1,061). Not a defect: the seam working both ways, and the M10d question it
      answers is that *pumped dry* needs the outlets past a divide or the mouth above the level.
      - [ ] **A sound effect for the quantum miner: *failed — insufficient charge*** (Matthew,
            2026-09-19, mid-play). The first sound in the game, so it wants a home for audio
            before it wants a file: the events are the three `M10: gate` prints in `Main`
            (`swung … with no pack`, `ran out mid-bite`, `the pack is spent`) and the fill's
            `refused whole`, all already edge-detected for the one-off print; the sound goes where
            the print goes. Godot's `AudioStreamPlayer` on `Main`, one stream, no bus work yet;
            whether a held Q on a dry pack re-fires it every 100 ms or once per hold is the
            pass's question (Alt's held chip went the same day; the hold is the only stream).
            Not scheduled — the cosmetics pass or the next play that asks for a second sound,
            whichever first.
- [ ] **After the optimiser (2026-09-15): re-judge `start.cmd`'s `--lightbudget=16` on a play** — it
      was a response to a 1.4 ms unoptimised derive that is ~0.36 ms now; and **re-take the Debug
      bench figures quoted against game numbers** (`-c Debug -- --support`'s 0.83 + 4.4 ms) before
      quoting them again. The release export with SQLitePCLRaw's natives is still the unverified
      step — worth one run, not needed for play.
- [ ] **M10e — the conductor index**: `conductor` block, `ConductorTracker` marking a dirty set and
      nothing else in the hook, per-chunk summaries at schema 5 riding the batch write, `NetworkSet`
      with position-derived ids, `maxRunCells` as occupied brush cells, the rescan cap, the
      forget-path rescan, the energised bit, the cut's leak; `--network`.
- [ ] **M10f — generators, converters, the crank and the sink**: `E` held with millis sampled,
      `burner`, the boiler that drinks and `consumed` on every identity, the engine beside it,
      `shaft`, media order, the dock, `pipe_outlet`, `flooded`.
- [ ] **M10g — recipes, stations, parts and the inventory screen**: the loader's second pass,
      jobs by value, the remainder per output, `unit`; **the inventory screen, folded in
      2026-09-18** — one `Control` on `E` tapped: the grid of every holding, the hotbar as a
      nine-slot assignment in the header at the next version — v6 as things stand, v5 having
      gone to the carried packs' charges the same day (a v5 world opens with its first nine holdings in
      ID order, so nothing moves), assign by number key from the grid and by drag, the placeable
      filter as the grid's (all / blocks / parts / machines) with the parts page its *parts*
      filter, the craft pane present within reach of a station, litres → m³ from 1,000 L, a total
      and no cap; `--assign=<slot>=<id>` and `--openinventory` the seams; the relay's own recipe
      retires M10a's seeding. Its pass reads the folded-in section first and puts the inventory
      half in its first session.
- [ ] **M10h — worldgen for the ladder**: WORLDGEN-NOTES' pass first, its three questions
      answered; logs and saplings, layers, seams and veins by a second lattice field.
- [ ] **M10i — the five tiers' JSON**; solar re-classed to `dc`.
- [ ] **M10j — the play**: the ladder climbed once, timed; five named moments as five sentences.

**The morning's plan, kept for the reasoning — its M10a–d are the parts above, as amended:** The pass's three
facts: the boundary is the machine tick (`Add`/`Remove` refuse mid-tick, so DESIGN's 20 Hz has no
consumer); a machine is a material plus a row (the body is 64 voxels of `overburden:relay_pump`,
so mesh, light, collide, support, mine, credit, hotbar and `Q`-placement at range are free; only
the config is new); and an outlet is out of the loaded box on day one (`ViewDistanceVertical` 2,
the played mine 64 m deep), so the unloaded-outlet rule is M10a's.

- [ ] **The eleven decisions**, each recommended unless marked: (1) the `machine` block inside the
      material JSON, `kind` an engine behaviour keyed by name, no second registry; (2) placement
      atomic, into water deferred to the boundary and displacing through `Lift`/`Return`; (3) the
      tick is the fluid boundary after the pour and before the table sweep, 20 Hz retired with the
      drill as its trigger; (4) the rows are the truth, read whole at open, written whole at
      autosave at schema 4, reconciled at every chunk arrival, dismantled by a dirty-chunk sweep
      that credits the remainder after a swing and nothing after a fall; (5) the pump draws Tier A
      when its sample's table stands over its top, else the wet cells in the 3 × 3 × 3 m around it;
      the outlet pours into the column above; an unloaded outlet on the surface inside the window
      credits the ledger, underground accepts nothing; `min(offered, accepted)` in one boundary,
      round-robin in position order; (6) ports are bounded integers in the JSON, the programmer is
      `E` on a machine until crafting makes it an item — **the key is Matthew's**; (7) the pump
      ships unpowered, priced in a material the player cannot yet make, seeded by a debug key;
      (8) power is a pool per channel with no wires, the first generator solar off `SkyColumn` and
      `OvercastAt`; (9) the carried battery gates the entangled miner by energy per voxel taken,
      header v4 — **the numbers are Matthew's**; (10) `pumped == emitted` per boundary on both tiers
      and across them, the M8 lifecycle battery with machines running as the test; (11) the split.
- [ ] **M10a — the entity, the pump and the outlet.** Decisions 1–5 and 7; the store slice (schema
      4, the reconcile) built and battery-tested before the pump moves a litre. `--pump=<L/s>`, the
      `M10:` line, `machines` on the `boundary` line, the bench's `--pump` arm — **the delivered
      rate against the nominal is the first number, and it decides the JSON's 100 L/s** (an
      estimate between the one-voxel hole's 29 L/s and the widest bore's ~300).
- [ ] **M10b — the programmer.** Decision 6: `MachinePanel`, the first input UI; `SetPort` in Core;
      channels and weights; weighted round-robin.
- [ ] **M10c — power.** Decisions 8 and 9: the pool, the solar panel, the placed battery, the
      carried battery, header v4, the gate on the entangled miner.
      - [ ] **The first consumer is a hand tool, settled 2026-09-03**: the entangled miner draws
            stored energy, must be recharged, and battery tiers are the ladder — Matthew: *"only
            once you have the latest tier battery will it be as effective as it is now."* Storage
            made portable; tiers as content the way pick tiers are. **Recommendation, recorded with
            the gate item under the chunk fill: a tier scales capacity and recharge, never the 4/s
            or the bite.** Decision 9 takes it as written.
- [ ] **M10d — the play.** Pump the played mine dry; the storm dimming the panels that pump it;
      the gate felt at the bite; whether a placed pillar reads as shoring (if not, a shoring
      material is one JSON file).
      - [x] **The outlets far from the player stall on their own mound — Matthew, 2026-09-19,
            underground: *"the water is building up on top of them … the water processing cannot
            keep up with the 'dump' rate"*.** The near/far order is the viewer's column and two
            rings, horizontal; an outlet 16 m away is far, its 250 L mouth layer is visited once in
            ~260 ticks, the pump stalls `no room` (4,809 by the end) and the hand-off never sees a
            rested cell there (`handed` moved only while he stood on the surface). **Built the same
            evening on his word: a machine's chunk column is near** — `MachineTick.NearColumns`
            (every pump's box at its radius, the box one cell around every outlet) handed to
            `FluidSimulation.SetNearColumns` once a frame beside the centre, `IsNear` the rings or
            the columns, a changed set re-classifying the carry once; the `M8b:` bracket and the
            profile's `fluidnear` line print the count. Six recipes, six caught; suite 1,652; the
            r6 control to the digit. Wire against the hand-over commented out: `--pumpoutlet=48
            --fluidnear=2` reads `4 machine column(s) … 0 far` against `0 … 10,336 far`. The play
            check was the mounds draining with him underground — **played 2026-09-19 the same
            evening: *"the mounds are draining now"*.**
      - [x] **`Alt+P`'s pack half broke the pool identity — the fresh world's first play,
            2026-09-19.** The cells' debug fill has been `entering` since c-2; the pack's went
            through `EquippedPack.Give`, the pool's recharge path, so a 7 kJ pack filled to
            3,600 kJ read `*** NO ***` on every boundary after — 1,403 `ERROR` lines in the log
            before the play's own numbers. The morning's pack was full at open and the fill gave
            it nothing. `MachineTick.FillPack` is the pack's door now, `PackDebugMillijoules`
            a term (`+ debug` on the line when it has), a swap after it stows the debug charge
            with the rest. One test; the bite oracle to the digit.
      - [x] **A fluid row naming an empty awake cell holed a chunk for good — the fresh world,
            2026-09-20 (small hours).** The reader refused it, the load job faulted, a faulted
            chunk job is not retried and draws as a hole — the black blobs under the compound's
            sheet. The unview and eviction writers listed every paused cell as awake; both filter
            to held cells now, and the reader drops an empty awake index and counts it (`M6:`
            lines, `awake index(es) dropped for holding nothing`). His row read 2 dropped of 18,
            471.9 L restored; the next open heals it. Two tests, four recipes caught.
            - [ ] **What drained a paused cell is unproved.** A visit never pauses an empty
                  cell, so the cell was held when paused and drained while its chunk was away;
                  the candidate is the hand-off taking from a cell the worker's solve levelled on
                  its captured view after the live terrain said the chunk left. If the `M6:`
                  count keeps climbing on a world written entirely after this fix, that is where
                  to look.
      - [x] **The evening's memory climb is garbage, not a leak (2026-09-21).** The 85-minute
            play read `managed` 948 MiB → 2.3–4.0 GB sawtoothing and the working set 1.7 →
            5.9 GB; `dotnet-gcdump` on the settled game read 159 MB live in a 971 MB heap —
            `_queued` 69 MB for 2.2 M active cells, the tick's lists 25 MB, the surface arrays
            17 MB. The rate is the surface's 33 MiB of arrays twice over plus the native mesh,
            four times a second; the per-panel update is the fix, and the `M2c:` line prints
            `live … at the last gen2` now. **The recipe**: `dotnet-gcdump collect -p <pid>` (one
            second on a 1 GB heap, the game barely notices), `dotnet-gcdump report <file>`.
            **Owed with the surface A/B below**: the working set's slope on the new build, and
            whether the native side (working set − managed − static, ~1.2 GB at the snapshot)
            falls with the churn or is the renderer's own.
      - [x] **The Tier B surface install on a played world — measured 2026-09-21 (evening) on a
            copy of `default`, 75 s from the open, both arms.** The open wakes everything, so
            the window shows the flood, not the rest: full 457/457 panels, default ~316/453,
            install 50.6 → 44.7 ms mean. **The finding was the walk: 650 ms of a 1,200 ms
            worker wall**, the sides pass over every held cell; cut to ~265 ms by the in-panel
            test inline and the full-layer perimeter shortcut (the worklog entry). Still owed,
            from his next play rather than a probe: **the settled state's `last rebuild N
            panel(s)` and `install mean`** on the `M8b:` line, and the `live` term's slope —
            the pass is a rebuilt count near the moving machines' panels and an install mean in
            single-digit ms. If most panels still rebuild every tick at rest, the cause is the
            solve levelling whole sheets a boundary (the item below), and the lever is there.
            **The world for it is `worlds/retired-spread-2026-09-22/`** (retired 2026-09-22 on his
            offer): 66.6 ML in 2,393 rows, 209 machines, 3.4 M active at its open, the surface at
            359k quads — the first run on the new build read `last rebuild 447 of 591 panel(s)`
            with the install at 92 ms mean because 75% of its panels move every tick at the open,
            and the settled state is what a longer run reads.
      - [ ] **The solve's pass on a 34 ML world with 191 machines is 1.6 s mean, 3.1 s longest**
            (2026-09-20, 64 pumps over 127 outlets, 2.25 M cells, 962k deferred, 4,282 of 4,295
            boundaries waited) — with the seeds bounded (66k a pass), the cost is the sheets it
            levels: fed and drained at every machine every boundary, so worth levelling every
            pass, and O(sheet) each. The M8b-3b "pass per boundary is O(sheet)" item proper.
            Levers, unpriced: level a sheet at most every N boundaries while it is being fed
            (the settle's own wait, per sheet rather than per chunk); cap a pass's cells and carry
            the rest to the next; a smaller share for very large sheets.
      - [x] **Rain lands on the player's material — planned and built 2026-09-22 (*"As recommended,
            build it please"*); Matthew: *"rain on my stuff should land in it"*.** His clay pond does
            not fill from rain because 98% of the rain is Tier A's and goes to the seed terrain's
            basin under the pond, and the 2% that falls as cells is scattered over the whole box.
            The pass is in [docs/M8-PLAN.md](docs/M8-PLAN.md), "Rain lands on the player's
            material — the planning pass": six facts, eight decisions, all recommended — rain over
            a column whose top is above its seed (`OpenAboveY > SeedSurfaceY + 1`) lands as a
            Tier B cell on that top, accrued per chunk column and cached against the sky column's
            revision, owed by the basin under it rather than the global debt, the 2% drops
            unchanged, the pending cap 64 → 256 with the backlog printed, `RainOnPlaced` in Core
            with its tests, a sub-count under `rained`. A 10 × 10 m floor takes ~40 kL an hour at
            the heaviest scaled weather. Not in it: rain into a dug pit (Tier
            A delivers through the portals — DESIGN's cheap route), rain into a walled pool on
            bare grass (the courtyard; *floor it* is the rule), a roof's run-off into a gutter.
            **Built**: `RainOnPlaced` in Core with eleven tests, `BasinRainfall.Divert(basin, µL)`,
            the forfeit past 64 cells a chunk column out of the vertical box (the build's one rule
            the pass did not have), the `rained` term's `onto the player's material`; read "Rain
            lands on the player's material — built" in M8-PLAN. **Play check, one sentence back**:
            the clay pond in the rain — does it fill at a rate that reads as rain, and does the
            `M8b:` line's `onto the player's material` climb while `handed` does not?
      - [x] **A walled courtyard on grass is the lake outside — Matthew, 2026-09-20: *"the water
            sheet appears to have become attached to the one outside … limit this to sheets
            without rock/dirt or similar between them"*.** M8b-3a's pinned trigger, fired: the
            hand-off reads the seed plane (a tank on natural ground leaks into its basin), and
            `LakeCeiling` and `WaterSurface` read seed heights against the lake's level, so a
            courtyard below the outside level is inside the lake — its air unholdable, its
            pour handed whole, the plane drawn through the walls. Not the sheet solve. **His
            decision, recommended as arm B**:
            - A. *A placed floor holds* — already true; the rule becomes "floor what you want to
              keep". No code. Does not match his words (walls, not floors), and the lake's plane
              still draws through the walls.
            - B. *The player's material encloses; nature's does not.* `Enclosure`: at the plane's
              Y, a 4-neighbour flood from the cell over the loaded voxels in which only a placed
              solid voxel (differs from the seed, not air) is an obstacle and natural rock is
              walked through; open if it reaches the bound (the loaded box's edge, or 64 m);
              enclosed otherwise. One predicate, three readers: the hand-off (an enclosed rested
              cell is not handed), `LakeCeiling` (an enclosed column has no band), `WaterSurface`
              (an enclosed sample draws no plane — or the plane only where the ring's dry
              sample is not enclosed). Cached per (x, z, Y) with the flood's whole visited set
              sharing the answer; invalidated by any edit inside the region's box (the pipeline's
              edit hook, per chunk). Costs stated: a natural pit below the lake stays the lake's
              (by Tier A's own model — it is underwater); three placed walls and a hillside read
              as open, because the hill is nature's and the flood walks through it (the fourth
              wall is the fix, a rule a player can learn); an enclosure larger than the bound
              reads as open; a wall with a gap under it on stepped ground reads as open (the
              flood finds the gap — and so does the water). Tests: a courtyard on grass keeps its
              pour with the lake above it; a natural pit inside the lake is still handed whole;
              a wall taken down by a bite re-opens it at the next boundary; the r6 control and
              the a-3 oracle to the digit (no placed voxels, so no flood ever stops).
            - C. Tier A reads the edited heights — rejected: M7's "no incremental watershed",
              and a 2 m sample cannot see a 0.25 m wall.
            **Taken — *"1 and 3 as recommended"* — and built the same night as arm B**, with one
            rule the pass did not have: **a wall claims its footing** (natural rock carrying a
            placed voxel at its seed + 1 is an obstacle at every level below), because the
            headless courtyard on the spawn's slope leaked at every level below its uphill wall's
            ground under the pass's rule. `Enclosure` in Core, the three readers, the hooks,
            `--enclosure=0` the control, `--courtyard=<m>` the provocation. Oracle:
            `--profile --speed=0 --courtyard=8 --injectlitres=20000 --measure=25` reads `handed
            0 L`, `6 flood(s), 6 closed`, and rests by tick ~85; `--enclosure=0` reads `handed
            20,000 L`. r6 and the a-3 oracle to the digit (a-3's enclosure line reads 4 open
            floods over 66k cells — the outlet body and the breach make the lake's columns
            non-quiet). Suite 1,671 → 1,689. **2026-09-22: the `enclosed` bracket says why a
            flood opened** — `N open at the bound, M of them out through natural ground; U at an
            unloaded chunk; farthest open / closed of 64` — because his clay pond's log read `116
            flood(s) / 0 closed` and could not say which rule. (It was none of them: the pond's
            floor is placed, its water is never on the plane, and it holds — rain was not landing
            in it. The item below.) **Play check, one sentence back**: build the
            courtyard's fourth wall on the lake's bank — the pool should stop being the lake's
            at the next boundary (`enclosed N kept` on the `M8b:` line, the plane gone from
            inside the walls) — and whether a courtyard on a slope reads right with its uphill
            wall on the higher ground. **The first play (2026-09-21) ran out of memory** —
            the cache unbounded, 5,884 regions as 16k entries each; a bitset a region and
            `MaxRegions` 4,096 now, measured flat on the flooded world (WORKLOG, 2026-09-21).
      - [x] **A material that rejects water — Matthew, 2026-09-20: *"i like porous rock … should
            we have a material type that rejects water?"*** Already decided in shape:
            permeability is per material since M1 and read since M8c-1, `SeepIndex` treats a
            voxel as seep-rock only above 0, every machine body ships at 0.0, and the material
            is named — MATERIALS-NOTES row 90, **clay**, *"the natural dam, and what a player
            builds a dam from"*, a worldgen band in WORLDGEN-NOTES with a trigger on M8c's
            decision 1. What it means here: seepage is the only way water crosses rock, so a 0
            material is a lining that stops the sweat; it does nothing for the courtyard (Tier A).
            Missing: the content and its source. Arms — **ship `clay.json` now, seeded off `P`
            like the machines** (permeability 0.0, hardness dirt's, a seventh `LayerColour`
            with its jitter entry or the game throws at first frame; the honest source comes
            with worldgen) — the lean, an hour; or wait for WORLDGEN's band. His word.
            **Taken the same evening — *"2 please"* — and built**: `clay.json` (permeability
            0.0, hardness 0.8, `supportDistance` 8, curve `[[1, 4], [2, 1]]` — an estimate),
            a twelfth `LayerColour` with its jitter, `P` seeding 64 cells a press, the
            content test and the old-world header test's count. Suite 1,662; r6 control to
            the digit. **Play check**: line the wettest wall of a room under the lake and read
            the `seep` line's faces fall by the wall's count; whether the colour reads as clay
            beside dirt. The worldgen band is still WORLDGEN's pass.
      - [ ] **Rain drops Tier B cells — Matthew, 2026-09-19: *"rain to randomly drop tier b
            cells from the sky too please … not many but just to make you think about the water
            runoff"*.** Built the same evening: `--rainshare=<percent>` (2, an estimate; 0 off),
            a cell at the top of the drawn box at a random column, owed to the sky and paid from
            its next rain in microlitres (`BasinRainfall.Divert`, `WaterSystem.DivertRain`,
            carried across windows). Suite 1,652 → 1,656. **Play check**: whether two percent
            reads as *"not many"* and whether a drop landing on a slope and running to the mine
            or the lake reads as runoff — the number to move is the share, one flag; and whether
            a single 0.25 m cell falling at 8 m/s reads as rain or as a dropped ice cube (the
            lerp draws it sliding; a bigger drop is four cells, a different shape). **Played the
            same evening: *"Looks good to me … I loved seeing the tier b water blocks falling"*
            — 2% stands.**
      - [x] **The settle's solve floods 294k cells a boundary on the played world** — worker wall
            750–870 ms mean, the tick at 1.2 Hz, 1,261 of 1,375 boundaries waited, the batch 3,514
            cells; the fresh world an hour in read the same (705 ms, 902 of 3,244 waited). Every
            carried cell was a seed every pass, and every seed is flooded three layers before the
            share can freeze its sheet. **Built 2026-09-19 (night) on *"go"*: a carried cell is
            offered once and again only when woken again** — the bit in `_queued`, `SolveSeeds`,
            `MarkCarryOffered`, the prefix kept across a re-classification. On a copy of the
            retired world: solve 92.8 → 5.3 ms a pass, worker 106 → 29 ms, 118k → 6.7k cells a
            pass, 229 → 276 sheets levelled. Three tests, five recipes caught, suite 1,660; the
            r6 control and every `--fluidnear=0` oracle unchanged. Read WATER-THREAD-PLAN's
            second 2026-09-19 amendment under slice 1. **Play check**: `worker wall mean` and
            `boundary(ies) waited` on the `M8b:` line under the same water — waited should stop
            climbing — and the pumps at their full rate.
            - [x] **The flooded `default` is retired as the measurement world** — *"Please
                  Retire my world for your own tests"*, 2026-09-19 18:31, after a clean quit:
                  `worlds/retired-flooded-2026-09-19/` (185 chunks, 969 fluid rows holding
                  21.1 ML, 312 machine rows, 615 banked basins; 2.6 MiB). The next launch makes
                  a fresh `default`. The lever above is priced against it on a copy, with
                  `--world=`; never opened as itself.
- [ ] **Deliberately not in M10**: shoring as a material (a pillar already carries a full budget
      under `SupportRule.Rest`), drills and conveyors and crafting (M11), the inventory's move to
      millilitres (v5 at M11, alone — not v4 with the machines as MATERIALS-NOTES said), random
      distribution, a machine cap (printed, trigger `machines` over a millisecond on the boundary
      line), wading and oxygen (the survival-mode trigger).

#### The entangled miner at 40/s — raised 2026-09-02, third time

- [x] **0.05 s → 0.025 s at Matthew’s request** — *"can we double the rate of the quantum miner?
      It’s just I am building the Taj Mahal but underground."* Same-session A/B at r4h/2v, 6 m/s,
      30 s, Debug, **both arms drawing**: 587 swings against 1164, **19.6/s against 38.8/s, a true
      1.98x**. Suite unchanged at 814.
      - [x] **Not the ~30/s the frame-boundary arithmetic predicts**, because headless frames are
            ~7.5 ms rather than 16.7. `_cooldown` is *reset* rather than decremented, so a swing
            fires only on a frame boundary — **expect nearer 30/s windowed at 60 fps.** A true 40+
            needs the cooldown subtracted rather than reset, **which is a pacing change for every
            tool and is deliberately not made.**
      - [x] **Remesh load doubled exactly** (31.5/s → 63.9/s, per swing unchanged at 1.6), which is
            the per-swing model being right rather than a surprise. *Faster is cheaper per swing*
            held a fourth time. `update ms` max unchanged at ~19.9 ms — **the tail is not this.**
      - [ ] **The number to watch has changed, and the source comment now says so: the main thread,
            54.21% → 68.47% of one core.** Debug at r4h/2v, and Release is ~8x cheaper on the light
            derive that dominates it — so not a ceiling, but **the first raise where a headline
            percentage moved by more than a rounding error. The next raise must be measured in
            Release before it is believed**, and the named ceiling is now 1.5x away rather than 3x.
      - [x] **Two fixtures failed first and neither looked like a failure.** r6h/3v @ 8 m/s measured
            the rate and **drew nothing**, so `remesh 0.0 per swing` was the absence of a
            measurement wearing the costume of one — it would have been quoted as *"the raise costs
            no remeshes"*. And **r4h/2v standing reported 142 swings in both arms**, identical to
            the digit: a stationary miner bores one tunnel and the ray stops hitting anything, so it
            is **rate-starved rather than cooldown-limited** and the fixture made both arms agree.
            **`--speed=0` exists because M5d-1 wanted the one deterministic arm, and that
            determinism is exactly what makes it useless here** — a seam built to remove one
            variable removed the variable under test.
- [x] **`L` + `Q` is protected in source, reported load-bearing by the play**: *"hitting Q then
      being able to place a light far off into the distance has been useful during the debug and I
      don’t want to lose the feature yet."* The doc comment said *"superseded by the brushes"* and
      **that is false at range** — `TryPlaceDebug` places at `MiningController.Hit`, which respects
      the *effective* reach, while right mouse is bounded by the player’s reach, and **reach is
      deliberately the player’s rather than the tool’s** (M3d, restated M4c). **Second debug bind
      whose value is the pairing rather than the bind.** **Revisit trigger: a placement verb that
      takes a reach argument**, which would make it expressible through the brushes and let it go.

#### The entangled miner takes a whole chunk — Matthew’s request, 2026-09-03

*"Adjust the quantum miner to take the whole chunk of the cell being mined. This is like, its got
superconducting coils and quantum vacuum based power supply and the thing weight tons"* — and, in
the same breath and unprompted, the constraint: *"but do not implement weight please."*

- [x] **`Carve.Chunk`, a third shape beside `Sphere` and `Voxel`, sharing the same `Take`.** While
      `Q` is on a bare click takes the whole 8 m chunk the ray hit — 32,768 voxels, 512 m³.
      Suite **850 → 863**.
- [x] **It reverses half of a rule recorded twice, and the surviving half is the one that
      mattered.** `Entangled` has said since M5d that entanglement *“changes how far and how fast,
      never how big a bite **or what the bite is hard enough to take**”*. The bite clause is gone;
      **the hardness clause is untouched**, so a chunk bite through bedrock leaves the bedrock
      standing and `Carve.Take` is still the only gate in the game. The source comment is corrected
      in place rather than deleted, because which half survived is the finding.
- [x] **No weight, and recorded as a decision so nobody adds it later as an obvious oversight.**
      512,000 L a click, no carry limit, no encumbrance. Matthew’s reasoning is the good version of
      the argument: the real-world answer to a haulage limit is *“zip tie two shopping trolleys
      together and drag that sucker behind me”*, and a player who cannot just walks back and
      forth — patience taxed and called difficulty. **Revisit trigger: a survival mode**, which is
      already the trigger for shortening reach; same decision, taken together or not at all.
- [x] **The cooldown stopped being per-*mode* and became per-*shape*, and the old argument does not
      reach.** The rule that a chip pays the sphere’s cooldown existed so precision could not become
      the *efficient* bulk method. Against 32,768 voxels a press nothing precision does competes, so
      pacing Alt to the chunk rate would protect nothing **and would cost the `L`+`Q` pairing
      reported load-bearing the day before**. Alt still chips at 40/s while `Q` is on.
- [x] **The highlight had to change, and it is not cosmetic.** A chunk boundary is drawn nowhere, so
      outlining the target voxel in front of an 8 m bite shows the player the wrong object — aim a
      hand’s width across an invisible seam and a different cube goes. **M6c-3’s brush-cell argument
      arriving a second time.** HUD says `WHOLE CHUNK (Q)` for the reason it says `ONE VOXEL (Alt)`.
- [x] **The bite is the brush cell as of 2026-09-14** — Matthew: *"just take an 8x8 cube like
      right click"*, then *"match the 4x4x4 grid on the right click function"*, then *"make it
      all the same 4x4x4"*. `Carve.Cube`, `CubeVoxels = BrushCell.Voxels` (4, 1 m) with
      `CubeOrigin` the brush's own snap, so dig and fill are one lattice by construction and a
      literal that agreed with the brush the day it was written is a recipe (caught by the one
      test that exists for it); the same `Take` and gate; `MiningController.Bite` an
      `EntangledBite` of Sphere / Cube / Chunk with Cube the default. **The fill moved too**: with
      `Q` on the right click places the selected brush at unlimited reach at the cube's rate, and
      only `--entanglechunk` pairs the chunk bite with the chunk fill. `--cubecooldown=` sweeps
      the rate (0.05 s, an estimate between the sphere's 0.025 and the chunk's 0.25;
      `PlaceController.BrushCooldownSeconds` follows it on the cube arm). Headless on the
      `--entangle --mine` arm: 17.8/s delivered against 20, 47 voxels a swing, swing 0.42 ms mean
      / 4.3 max, 1.7 remeshes a swing. 22 tests; three recipes at this size, three caught. The
      first cut was eight voxels at 10/s (9.4/s, 379 voxels, 1.3 ms) on a misreading of "8x8".
      - [x] Rate: 10/s, Matthew's — *"10/second eventually this will cost a lot of power"*. The
            rate is an input to M10's battery gate (the entangled miner's power cost is the gate
            that makes you earn it), so it is chosen for what a charge will buy, not for the frame.
            `--cubecooldown=` still sweeps it.
      - [x] **One click is one click** (same evening): on the cube arm a bare press fires once
            and never repeats while held, both buttons; the cooldown underneath drops a click
            inside it rather than queueing it. Alt keeps its held repeat — chipping at range is
            the L+Q fine-work case — and the sphere and chunk arms are as they were.
            **Amended 2026-09-19: Alt's repeat is gone and Q held is the stream** — the modifier
            map read back had Alt meaning crawl in fly *and* a 40/s voxel stream on the cube arm,
            so a crawl with one click sprayed voxels; *"holding q is supposed to enable
            automine"*. Q tapped toggles the mode (on release now, ≤ 0.25 s late), Q held past
            the threshold runs the miner from either mode — `TapOrHold`, `MiningController.Running`,
            `--holdafter=`, `--holdq`. A click is one click whatever it holds. **Unplayed**; the
            play check is under M10d.
- [x] **The rate is measured, and the measurement says the rate is not what needs choosing.**
      Same-session A/B at r4h/2v, 6 m/s, 30 s, Debug, against `--entanglesphere`: **one bite costs
      `swing ms` mean 16.9 / max 35.6 on the sim thread against the sphere's 0.054 / 2.48** — one
      to two whole frames — and **no cooldown can bound it, because one bite is atomic.** M2d's
      mesh-apply finding a third time. At 3.9/s that is **6.6% of wall clock inside the carve**,
      and the world still streams: 184 drawn against 194, first draw 5.12 s against 4.93 s.
      - [x] **Per voxel the chunk bite is 4.3x cheaper** (0.70 µs against 3.0), because one
            raycast, one remesh marking and one relight queueing amortise over 23,964 voxels
            instead of eighteen. **Fifth outing for "the bigger arm is cheaper per unit."**
      - [x] **The whole-run main thread went *down*, 65.95% → 52.53%**, because an emptied chunk
            meshes to nothing — quads 83,004 → 73,714, derives 13,679 → 9,618. **Stated with its
            confound: the arms removed 66,560 m³ and ~333 m³**, so this is what two rates cost
            rather than evidence one shape is cheaper.
      - [x] **`frame ms` told the truth for the first time in five outings**, and the rule sharpens
            rather than reverses: max 38.5 is accounted for by a directly-timed `swing ms` max of
            35.6, with the sphere arm's flat 7.9 as control. **Unreliable as a *detector*;
            corroborating when a directly-timed number of the same magnitude exists.**
      - [ ] **Named lever, deliberately not built: a chunk-aware write path** — fill the palette
            with air once, update the sky column once, mark the neighbourhood once, rather than
            32,768 `SetVoxel` calls each doing all three. ~2 orders of magnitude. Not built
            because this is a debug bind and it would be a second write path beside the one thing
            M6 says must never be wrong. **Revisit trigger: the hitch reading as a stall in play.**
- [x] **0.25 s ships and halving is recorded rather than done — Matthew, 2026-09-03**: *"maybe halve
      the mining rate, like, or even quarter… or not"*, then *"I don't want to adjust the goalpost
      mid run."* **The right shape is `rainfallScale`'s** — the measurement bounds the range and a
      person holding the thing picks inside it. `--chunkcooldown=` is the seam, and it is
      deliberately **not** profile-only, because `Q` is a play bind.
      - [ ] **The argument for halving corrects a claim the measurement invited.** *"The rate
            chooses the duty cycle and nothing else"* is true of **cost** and false of **feel**:
            when one bite is a whole frame, **the duty cycle *is* the felt smoothness.** At 4/s
            two or three frames go every 250 ms with none clean between; at 2/s the hitch is
            identical with four clean frames around it. **And 250 ms is inside human reaction
            time**, so "stop, that is enough" costs two chunks — 1,000 m³ — of overshoot.
      - [ ] Against it: 4/s clears M8's own 96 m × 96 m × 24 m bench mine in **1.8 minutes**
            against 3.6 at half and 7.2 at a quarter, and the tool exists to stop digging being a
            chore. **Revisit trigger: the first play that holds the button.** **Fired 2026-09-03,
            holding it: *"4s yeah feels fast enough."* 0.25 s stands and the halving is closed.**
- [x] **An instrument defect found by arithmetic, predating this change by six milestones.** The
      chunk arm reported **4.33 swings/s against a nominal 4.0**, and a cooldown that is *reset*
      rather than decremented can only deliver *fewer* than nominal — **a rate above its own
      ceiling has to be a denominator.** `StreamingProfiler.MeasuredSeconds` excludes the 3 s
      warm-up and the pipeline's counters do not.
      - [x] **Every per-second figure with a *counter* numerator was high by
            `(warmup + measure) / measure`** — **10% at the 30 s windows this project uses, 20% at
            the 15 s default.** `63.9` remeshes/s and `54,514` cells/s in the worklog, among others.
      - [x] **Per-swing and per-chunk figures were never affected** (both counts lifetime, warm-up
            cancels), and **the histograms were always right** (`update ms`, `swing ms`, `light ms`
            are sampled only while warm). **Two kinds of number shared one denominator and only one
            of them wanted it. So every recorded *conclusion* stands** — the entangled miner's
            "1.98x" is a ratio of two identically inflated numbers.
      - [x] Fixed with `StreamingProfiler.RunSeconds` at the three counter sites. **The report now
            prints swings/s beside the swing count**, because every previous cooldown raise was
            checked by dividing by hand in a worklog entry — exactly where this went unnoticed.
      - [x] **And it now says when the hardness gate never fired.** Both arms show `0 too hard`
            because `steel_pick` takes everything in the spawn terrain: **a detector that did not
            run, not a gate that passed.** `VoxelsRefused`' own remarks have said so since M3d
            without the report ever saying it.
- [ ] **Spotted and not chased**: the sphere arm reported `light settled 0.19 s` against
      `first draw 4.93 s`, which is internally impossible — nothing meshes until its neighbourhood
      is lit. Pre-existing, unrelated to this change, and **a reading not to quote** until someone
      looks at the detector.
- [ ] **A break-recipe that came back green and is an identity rather than a hole**: deriving the
      chunk from `SwingCentre(hit)` instead of `hit.Voxel`. `SwingCentre` *is* the centre of
      `hit.Voxel` and `FromMetres` floors back to it, so the mutation swaps a value for itself.
      **Worth keeping because the identity is conditional on a decision with a live documented
      alternative** — `SwingCentre`’s own remarks argue against the ray’s entry point, and if that
      ever reverses this shape would be right by luck rather than on purpose. Fourth outing for the
      zeroable-epsilon category. The recipe it was reaching for is `hit.Previous.Chunk`, which is a
      different chunk exactly when the hit is on a boundary — i.e. whenever you are looking at a
      wall.

#### The draw distance goes to 48 m — Matthew’s request, 2026-09-03

*"I want to expand the draw size in chunks, by two on each side"*, with *"dont expand the water
distances please unless its tied."*

- [x] **`ViewDistanceHorizontal` 4 → 6**, and **measured before changing** because this is the axis
      M5d-1 found a cliff on. Debug, headless, 20 s, `--vradius=2`: at 6 m/s, **main 30.74% → 45.66%,
      drawn 167 → 335, first draw 3.80 s → 6.66 s**; at 20 m/s **both radii draw zero** and r6 is
      fractionally cheaper (74.16% vs 75.29%).
- [x] **The headline is a null of the useful kind: it does not create a new failure mode.** The
      20 m/s cliff is M5d-1’s, has been there since M5, and is light deriving on one sim thread
      rather than anything about view distance. **The raise spends headroom that already existed** —
      which is why the fast arm was worth running rather than only the flattering one.
- [x] **No water distance moved, checked rather than assumed.** Watershed analysis 512 m
      (`_waterPitch` 128 x 4 m), rain occlusion field 64 m (`SkyHeightField.SpanMetres`), rain box
      48 m (`Rain.BoxHalfWidthMetres`) — all fixed constants, and `ViewDistanceHorizontal` has six
      use sites of which none is hydrological. **Interaction, not coupling:** the drawn world now
      reaches exactly as far as the rain box instead of stopping 16 m short; the 64 m field still
      clears the 48 m box by 8 m a side.
- [x] **The recorded baseline moves, and that is the expensive part.** New default **2601 viewed /
      325 drawn / 3446 jobs at 411 quads**, settled ~8 s; **the historical 1521 / 165 / 1926 at 404
      needs `--radius=4 --vradius=2`.** **Quote the radius with any streaming figure from here on.**
- [ ] **Vertical stays 2** — a separate knob, and 2 → 4 would compound to ~2.5x the chunks rather
      than 1.71x. Unmeasured, so unchanged. **Revisit trigger: wanting to see further down a shaft.**
- [ ] **The 20 m/s cliff is now more visible, and is still M5d-1’s to fix.** Two levers were measured
      at M5d-1 and neither is built (`PaddedChunk.CopyFrom`, 60–75% of the derive; and moving the
      derive off the sim thread). **At r6 there is more world to catch up on when you stop**, so the
      recovery is longer even though the cliff itself is unchanged.
      - [x] **The sprint corridor, measured 2026-09-06** (worklog, same date): thirty chunks at the
            on-foot 7 m/s draws throughout at r6 (152 generates / 260 derives / 52 meshes a chunk
            of travel, 56% of a core); at the flycam's 30 m/s **only r1h/1v draws — 25 of 27 under
            the 16 ms budget, 10 under the 4 ms default — and r2, r3 and r6 draw nothing at any
            budget**, the derive exceeding a whole thread. Halving the radius buys nothing; the
            27-chunk floor is a cliff edge.
      - [x] **Adaptive view radius — Matthew's proposal, 2026-09-06; planned and ✅ built the same
            day, all six decisions as recommended. See [docs/ADAPTIVE-RADIUS-PLAN.md](docs/ADAPTIVE-RADIUS-PLAN.md),
            "Built".** `ViewRadiusPolicy` in Core, applied by Main through `UpdateViewer`; shrink a
            ring while moving with the derive backlog over two slabs of the current box; **grow a
            ring while the backlog is empty and the light step's averaged share of its budget is
            under 0.5** — not the plan's dwell, which the 7 m/s corridor broke on its first run (a
            walker crosses a chunk every 1.1 s and never dwells: 20 drawn against 344); a grace of
            one cadence after any change. Seams `--adaptive=1` (default off, `start.cmd` passes it),
            `--minradius=`, `--shrinkslabs=`, `--growbelow=`, `--cadence=`, `--sprintfor=`. Suite
            1,058 → 1,073, six recipes caught. **Measured: 30 m/s draws 25 of 27 at the floor
            against 0; 7 m/s ends identical to fixed with first draw 0.81 s against 6.47; a
            standing start draws at 1.00 s against 6.65 and is grown at 14.5 s for +69% derives;
            sprint-and-stop is grown nine seconds after the stop.**
            - [x] **Play check ✅ 2026-09-07 — Matthew: *"fly holding ctrl is the only way I can
                  beat the loader/generator now, and honestly I am happy with that."*** The logs:
                  r3 by the first 5 s report, r6 from 35 s, `grown 30.56 s` with the flood on open
                  and `grown 14.67 s` with the water restored paused; the deepest sampled shrink
                  was r5 (271 and 628 chunks in flight), back at r6 within two 5 s reports — a
                  floor visit under ten seconds of Ctrl falls between samples. The 30 m/s sprint
                  outrunning the world is accepted rather than fixed. Whether the fill-in reads as
                  popping went unremarked — silence, and still the plan's first revisit trigger.
            - [ ] **The ring sweep is 22–29 ms in one frame** (576 records at r5 → r6, columns
                  seeded), five per fill. Lever: stage a grow one face per frame, or seed columns
                  on a worker — the cache slice 1 finding that the sweep is mostly `SkyColumn.Seed`.
                  Not built until a play feels the hitch.
            - [ ] **`--growbelow=0.5` is conservative in the 0.5–0.78 load band**: the 20 m/s
                  turnaround at a 4 ms budget stopped at r5 (load 0.81) where fixed r6 keeps up at
                  44%. At play's 16 ms budget the same arm grows to r6. Sweep in play if the box
                  reads as smaller than the machine can afford.
      - [ ] **A third, voxels-only margin ring — the largest lever the corridor found, and a
            planning pass before it is believed.** Every chunk derives ~1.8 times while moving
            (1.71 at r6/7 m/s, 1.95 at r1/30 m/s; exactly 1.0 standing): the leading slab derives
            against an absent forward neighbour read as air, then again when its voxels arrive —
            the gain re-derive cache slice 2 relies on. A ring that generates and never derives
            lets ring 2 derive once with every neighbour present: **+37% generation per chunk of
            travel on workers that spend half their takes finding nothing, for −45% derives on the
            thread that is the cliff**, exact in the visible box by the margin's own 15 m against
            16 m argument. Tracked chunks +53% (3,971 against 2,601 at r6), ~2 KB each, no grids.
            **Read M5c-1's "light needs no third margin ring" first**: that argument was about the
            barrier ladder and a ring that *does* derive; this ring would not, and the two have to
            be shown compatible before a line is written.

#### The entangled miner fills a chunk too — Matthew’s request, 2026-09-03

*"How hard to add fill cluster to the quantum miner? Like if its easy just go ahead."* It was easy —
`Place` and `Carve` have been deliberate twins since M6c-1 — **and the one place the mirror does not
hold is the whole of getting it right.**

- [x] **`Place.Chunk`, a sixth shape sharing the same `Put`.** While `Q` is on, a bare **right**
      click fills the whole 8 m chunk in front of the crosshair — 32,768 voxels, 512 m³,
      **512,000 L**. Suite **869 → 888**.
- [x] **The bite and the fill name different chunks from the same aim.** `Carve.Chunk` takes
      `hit.Voxel`’s chunk; `Place.Chunk` takes `hit.Previous`’s — the same split that has separated
      `Place.TargetVoxel` from `Carve.Chip` since M6d-1, one scale up. **Mining names the thing you
      point at, building names the space in front of it.**
      - [x] **Copying the mining half breaks the verb precisely where it is needed.** Stand in a
            chunk you bit out, look at its far wall: the wall is the *neighbouring* chunk, so
            `hit.Voxel.Chunk` would report **32,768 `Occupied` having written nothing** — broken in
            the one situation it exists for, fine in open air where nobody needs it. **Caught by 3,
            two of them scenario tests rather than unit ones**, because a hit that does not straddle
            a boundary is a hit at something other than a wall.
- [x] **Only air is replaceable, and at 512 m³ that stops being a tidiness rule.** M6c-1 argued it
      as a hardness-gate defence; a fill that overwrote would be a key deleting half a thousand
      cubic metres with no tool and no gate — **strictly worse than the bite, which at least still
      asks the tier.** Dropping it fails **6**, three of them pre-existing brush tests.
      **So the entanglement clause that survives is now symmetric across both verbs: the mode
      changes how far, how fast and how big, and never what a verb may overwrite.**
- [x] **Predicted a hole here and was wrong, recorded as such.** Permitting `Place` to place *air*
      is **caught by 3, two of them pre-existing `PlaceTests` cases** — `Validate` is shared and
      already covered for `Cube` and `Voxel`. The chunk-overload test is belt and braces
      (**M4a’s zeroable-epsilon category, not M5c-2’s missing wire**), worth keeping because this
      is the overload where placing air deletes 512 m³ with no gate.
- [x] **The one genuine hole: `TargetChunk` by truncating division came back GREEN.** Every other test
      names its chunk directly and both seam tests straddle a *positive* boundary, so **nothing
      exercised the one conversion that can round — voxel → chunk.** `VoxelPos.Chunk` shifts, so
      voxel −1 is chunk −1; truncating puts it in chunk 0 and collapses two chunks into one,
      filling the wrong 512 m³ every time somebody builds west of or below spawn. Closed by
      `AnAimBelowTheOriginNamesTheChunkBelowTheOriginRatherThanChunkZero`. **The bite’s battery
      recorded this exact asymmetry and its own version of the recipe was an identity — here the
      same reasoning found a live defect, because the fill actually calls the conversion.**
- [x] **The body refusal earns its keep rather than merely existing.** A 1 m brush can be aimed away
      from yourself; **an 8 m cube centred on the chunk you are standing in cannot**, and the
      ordinary use of this verb is refilling the hole you are stood in. Given its own recipe rather
      than trusted to the shared `Put`, because nothing else in the file would notice it go.
- [x] **One cooldown for both verbs, and `--chunkcooldown=` paces both.** One fill and one bite are
      the same 32,768 `SetVoxel` calls, so **a person tuning the felt rate of the mode is tuning one
      thing**; two numbers for one feel is what `ReachMetres` already argues against. Precision
      keeps its own fast rate, for the reason the bite gave.
- [x] **It reaches as far as the bite, which closes a recorded revisit trigger head-on.** The M7
      play called `L`+`Q` load-bearing — *“hitting Q then being able to place a light far off into
      the distance has been useful”* — and the note said the pairing existed because **right mouse
      was bounded by the player’s reach while `L` was not**, trigger being *“a placement verb that
      takes a reach argument”*. Alt+right at range is now that verb; `L` still works and is still
      faster.
- [x] **A three-scale outline.** The cell highlight becomes the 8 m cube while the mode is armed,
      for the reason the mining outline does: a chunk boundary is drawn nowhere, so a 1 m cell
      outline in front of an 8 m fill shows the player the wrong object. HUD reads
      `WHOLE CHUNK (Q) · dig / fill`.
- [ ] **`OutOfStock` stops being an edge case, and it is an unplanned balance mechanism.** A bite
      credits 512,000 L of *mixed* stone, dirt and seam; a fill spends 512,000 L of *one*. **So a
      chunk generally cannot be put back with what taking it yielded**, and running dry part-way is
      the ordinary outcome. **Recorded as an observation rather than a decision, because nobody
      chose it** — no weight, no encumbrance, that ruling stands untouched, but the fill is still
      bounded by having mined enough of one thing, which is the tax a mining game should charge.
      **Revisit trigger: the first play that tries to fill in a worked-out chamber and finds the
      limit tedious rather than motivating.**
- [x] **The cost is measured, 2026-09-03, and the prediction was right about the shape and wrong
      about the size.** r4h/2v, spawn, `--place --entangle --stock=2000000 --pitch=-15`, Debug: 70
      fills, the first putting 28,543 voxels into a chunk 87% air, the other 69 hitting the block it
      made — 32,768 `Occupied` reads each. `place ms mean 3.434 p50 2.576 p95 7.033 max 8.234`.
      **A fill into air ≈ 8.2 ms, a fill into solid ≈ 2.6 ms, against the bite's 16.9 / 35.6.** The
      profile is inverted as predicted, **but the dear case is half a bite**: `Put` credits nothing
      per voxel and asks no hardness, and the bite's per-voxel `Take` is the expensive half. The
      standing arm is degenerate after its first fill for the reason the standing bite arm was — the
      aim meets its own product.
      - [ ] **The fill's loop order is the pessimal one for the sky column and the bite's is the
            optimal one, and it is the same loop.** Y ascending, a bite moves each column's open
            height once and a fill into air moves it 32 times — 32,768 `SkyHeightChanges` against
            1,024, deduped at `QueueLightBuild` so the waste is lookups rather than rebuilds.
            Descending Y in `Place.Chunk` is one line. **Not done: the number that would justify it
            is a felt hitch and there is none.** The counter reads 32× high on a fill.
- [ ] **The chunk-aware write path would now pay off twice rather than once, and is still not
      built.** Reason unchanged and stronger: a second write path beside the one thing M6 says must
      never be wrong, now with two callers who each have to be right about it. **Revisit trigger
      unchanged: the hitch reading as a stall in play, or this becoming content.**

#### The played world — 2026-09-03, what 4,584 stored chunks measure and what is not on the plan

Matthew: *"I've just been playing it mining out huge chunks … tell me if you can suggest anything
about what we are doing here that is not on the plan."* Worklog entry of the same date has the
numbers; these are the tasks it surfaced. **Endorsed by Matthew the same day — *"Like the 5
points too minus point 3"* — where point 3 was the gate as a game-mode dependant, since superseded
by the battery item below. The rest are wanted, not merely suggested.**

- [x] **M6's load-path concern is retired for the shape the chunk miner produces.** 4,584 stored
      chunks against M6d-2's 176 and the predicted 2,505 crossover. Standing start at the saved
      position, r6h/2v, Debug: **953 serial loads, light settled 5.97 s against 5.88 s generating**;
      falling 120 m at 40 m/s through it, **1,528 loads and `held 0`** in both arms. **Stated
      caveat: every one is a 52–63 B uniform blob**, the cheapest deserialisation there is.
      **Revisit trigger: thousands of *non-uniform* stored chunks**, which only partial edits at
      scale produce.
- [ ] **A profile taken inside the mine reads as a stall and is not one.** The load arm stood inside
      ~100 m of bitten-out air; 923 meshes came back empty, so `first draw` never fired. The report
      should say when every visible mesh was empty. `light settled` is the comparable number.
- [ ] **`water … L per wall-clock minute` divides a lifetime total by this run's seconds** — printed
      313,678,347 L/min on a world carrying 94 ML from five hours of earlier sessions. Its comment
      says the rain has been falling "since the world opened", which is true of a new world only.
      Wants the delta since open. Same family as the `RunSeconds` denominator.
- [x] **M8's cost model was derived from tunnels; the play has made chambers the normal case.
      Measured 2026-09-03 at M8b-1 — see its block: the floor settles, the roof falling in is the
      largest tick there is.**
      M8-PLAN's ~14,400 cells and ~50,000-cell revisit trigger come from SCALE's cave, and M8a's
      bench mine is `hand_pick` bores by construction. **One bitten chunk under a lake is 32,768
      cells of void; this world has 4,562.** Before M8b settles a tick budget: a second `--fluid`
      fixture shape — an 8 m cube, then 2 × 2 × 2 of them — and the settle on an 8 m × 8 m floor.
      **The chunk bite is M8b's provocation instrument**: a pit under a lake is the breach test, and
      at 8 m it registers on the 4 m grid where a bore cannot.
- [ ] **M9's planning pass must take the chunk bite as its worst case**, the way M8a took the
      lake-into-shaft. A bite makes an 8 m unsupported span in one frame at 4/s; a fill puts 512 m³
      in mid-air with nothing anchoring it, which under any support rule is rubble unless shored.
- [ ] **The gate that makes you earn it is a battery, and it attaches to M10 rather than to a game
      mode.** Matthew, 2026-09-02: the chunk bite is *"totally endgame like, we're talking you've
      been to space and back"* — 2026-09-03: *"space and back was like a figure of speech like I
      meant ima make you work to get this"* — and then the mechanism: *"this tool will eventually
      have an energy cost and must be recharged and like only once you have the latest tier battery
      will it be as effective as it is now."* **So not a third thing waiting on a game-mode concept
      — that count goes back to two** — and not a currency to invent: it is M10's *storage* node made
      portable, the first power consumer that is a hand tool rather than a placed machine, and
      battery tiers are content the way pick tiers are (non-negotiable 5). It is also what keeps
      *the mine is the antagonist* true against a tool that deletes 512 m³ a click, and it puts the
      tool inside DESIGN's loop — *needs power, which needs more mining* — instead of exempt from it.
      **Recommendation, his to hand back: a tier scales charge capacity and recharge rate, never the
      4/s or the bite** — the rate is the measured duty-cycle bound and he says it feels right, the
      whole chunk is the tool's identity, and bites-per-charge is what a battery *is*. Energy per
      bite proportional to voxels actually taken, which `CarveResult` already counts. Costs one
      persisted number, the charge, which rides M10-NOTES' header v4. Planned at M10, not before.
- [ ] **The hotbar's unit has been outgrown.** `1,767,903,281 L` in an 84 px slot, printed `N0`; the
      format was chosen for a 265 L swing and a bite is 512,000 L. A scaled unit — m³ past a
      thousand litres, or the chunk — and **the unit is Matthew's to pick.** Scheduled at M10g
      with the inventory screen (2026-09-18, decision 6 of the folded-in section: m³ from
      1,000 L, recommended over the chunk).
- [ ] **The window-edge outlet now moves millions of litres, and the magnitude is what is new.**
      379,831 L and 4,027,831 L "would not fit back" on two window moves. Designed — the edge is an
      outlet, and `Seed` leaves rather than carrying a debt — and zero in every test and both plays
      until a world had 92 ML banked. Symptom to ask about: a lake near a window boundary dropping
      when the player crosses one.
- [x] **Asked of the play and answered the same day, in his words.** The water surface over the pit:
      *"lakes overhanging yes"* — the shape M7's play left unconfirmed, confirmed, and it reads as
      DESIGN's *edits do not change the watershed at M7* rather than as a defect; **M8b's second
      play-confirmed motivation**, after the pond above a tunnel. The rate: *"4s yeah feels fast
      enough"* — 0.25 s stands, the halving is closed. A lake dropping at a window boundary: *"um
      did not notice any falling"* — **silence rather than evidence**, since x20 rain refills it and
      the window moves only every 128 m; the 4 ML the profile saw left on a 100 m teleport that no
      play makes. Nothing was reported as a stall, and nothing was asked about one.

#### The quantum machine family — Matthew’s proposal, 2026-09-02

A **quantum fluid pump**, a **quantum pump output** that it teleports water to wirelessly, several
outputs selected randomly or programmatically, and eventually a **quantum machine programmer** — a
general config interface mapping input numbers to output numbers, for any machine. Full reasoning
and eight open pieces in [docs/M10-NOTES.md](docs/M10-NOTES.md). **Recorded the day it was raised,
on the deferred-collapse precedent**, so M10’s planning pass starts from it rather than re-deriving
it.

- [ ] **The structural reason it is a good idea is not the one it looks like: teleport deletes
      pipes.** A conventional pump implies a network to solve, junctions to split, invalidation on
      every edit, and **fluid in transit** — which is more of exactly the thing M8 has to keep
      terminating. Teleport is a subtraction and an addition: no transport, no graph, no in-flight
      state. **Fourth outing for *the cheap version is also the more accurate one*** — a pipe
      network approximates transport badly, a teleport declines to have any, which is a coherent
      rule rather than a lossy one.
- [ ] **It is the one subsystem whose worst case is knowable in advance**, which is the strongest
      argument for building it early: M8’s active set is bounded by terrain and M9’s by the mine’s
      shape, but **a machine layer is bounded by a placement count** — a number the player chooses
      and the engine can cap. That is also why Matthew’s framing holds: *“if it’s proven that the
      structural additions cannot be met due to CPU load, it is still going to work out great.”*
- [ ] **The biggest thing in the proposal is not the fluid: a machine is the first *entity*.**
      Everything the world holds today is either a voxel (a material ID, regenerable from the seed)
      or an aggregate number (litres per basin, a sky height per column). A machine has an
      identity, a position and **configuration the player authored**. So it needs its own table
      beside `chunks` and `basins` — **header v3 → v4, additive, migrating under M6c-2’s policy** —
      and **M6’s “the tests are the only thing between a defect and permanent data loss” applies
      harder here than it did to chunks, because a chunk can be re-dug and a config cannot be
      guessed.**
- [ ] **Backpressure is the piece most likely to be got wrong, and it is not a detail**: M8’s
      oracle is that volume is conserved with **no tolerance**, so a machine that quietly drops
      litres breaks the property the whole fluid milestone is verified against — silently, because
      a missing litre looks exactly like water that has not arrived yet. **Recommended: move
      `min(available, acceptable)` and do nothing else.** No buffer, because a buffer is in-flight
      fluid by another name; no spill, because that is a second invisible source of Tier B
      activity. **The test belongs in M8’s volume oracle with machines running**, not in a machine
      test.
- [ ] **Random selection must be seeded and server-authoritative, or it is not an option** — the
      rule M9’s deferred collapse already recorded, per non-negotiable 1. **Recommended:
      round-robin as the default and weights via the programmer, with round-robin as the all-equal
      case**, so there is one mechanism rather than two.
- [ ] **Pairing by channel number, defaulting to 0.** It *is* the “input numbers to output numbers”
      idea, so the programmer has a job on day one. **Rejected: an explicit stored link**, which
      creates a reference that must survive one end being mined, a reload and a chunk unload.
- [ ] **“Wireless” runs into the analysis window, and `BasinLedger` is already the answer** — a
      teleport into an unloaded region credits the ledger exactly as a departing window does.
      **Second time that design has paid off somewhere it was not written for.** The open
      sub-question is whether an output in an unloaded chunk still exists, which is the entity
      question above. Related: the entangled miner already recorded that *“however far away” means
      “as far as the world exists”*, and that settling reach-as-content is due when it stops being
      a debug bind.
- [ ] **The programmer is a format, not a screen.** A machine declares its configurable ports in
      its own JSON and the programmer renders what it finds — so a third-party machine gets a
      config UI with no privileged path, which is **non-negotiable 5 stated as a test**.
      M6d-3’s derived-label rule and M3c’s bound-every-mod-authored-number rule both apply
      unchanged.
- [x] **Naming settled 2026-09-02: industrial, not physics.** Raised as the last free moment,
      because non-negotiable 2 says IDs are never renumbered and a removed mod leaves a hole that
      stays. **Recommended set, and one word from Matthew to change until the first machine
      ships**: `minercraft:relay_pump`, `minercraft:relay_outlet`, and `minercraft:machine_programmer`.
      - [ ] **`relay` because a relay already means “sends onward with no physical path”**, which
            is the whole mechanic, and because **radio relays have always had channels** — so the
            channel-number pairing rule stops needing an explanation. **The programmer is
            deliberately not part of the family**: it configures *any* machine, so a family name
            would misdescribe the one component whose generality is the point.
      - [ ] **`entangled_pump` was the near miss** — it would have given the game one coherent word
            for *acts at a distance*, echoing the entangled miner. Rejected because it is a physics
            word, and because **the entangled miner is a debug bind whose promotion to content is
            still an open decision**: naming shipped content after an unshipped debug tool binds
            the two before either decision is made.
- [ ] **Interaction with M11’s “drills, pipes, conveyors”, which wants deciding rather than
      drifting.** If teleport is the fluid answer, pipes are either redundant, or they become the
      *cheap early-game* transport that quantum machines are the upgrade from. **The second reading
      is better game design and costs nothing to adopt now**, but it means M11’s pipes are a
      deliberate tier rather than the default way fluid moves.

### M11 — v1.0

- [ ] Drills, pipes, conveyors
- [ ] Crafting, health, hunger
      - [ ] **Recipes are the chemistry set** — litres in, litres out, with power and time, on M10's
            machine framework; the volumetric inventory makes stoichiometry a ratio rather than an
            item count. The element table and the battery ladder (lead–acid → nickel–iron →
            lithium from pumped brine) are in [docs/MATERIALS-NOTES.md](docs/MATERIALS-NOTES.md).
      - [ ] **The periodic table as data underneath, never as blocks** — Matthew, 2026-09-03. All
            118 in `elements.json` (no texture, no shader, `state at STP` excludes gases for free);
            a material declares a composition in element mass-parts; recipes are checked for
            element balance at load, with a stated tolerance, the way the tool ladder is checked
            today. A general *extract element X from material M* rule then covers most refining
            without a recipe. **Cost named: the inventory leaves whole voxels for millilitres** —
            M6c-2's revisit trigger, arriving from crafting — and rides header v4 with the machines.

### M12 — Modding tier 2

- [ ] MoonSharp host, sandboxed
- [ ] Event bus exposed: `BlockChanged`, `ChunkGenerated`, `PlayerTick`, `ToolUsed`,
      `RecipeCrafted`
- [ ] Mod loading order, dependency declaration, API versioning
- [ ] Write a non-trivial example mod as the real test

### M13 — Polish

- [ ] Distance LOD (downsample distant chunks 2x/4x by majority vote)
- [ ] Biomes
- [ ] Gas pockets, darkness as a hazard
