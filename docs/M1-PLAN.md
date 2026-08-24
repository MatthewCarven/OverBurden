# M1 — Storage, identity, and the mesher

> Planning doc. See [DESIGN.md](DESIGN.md) for the decisions this implements.
> Written 2026-08-24.

M1 was originally one milestone. It is really four, and they fail in different ways:

| Part | Problem | Failure mode if rushed |
|---|---|---|
| **M1a** Identity and registry | Stable, mod-extensible IDs that survive a save/load | Modding is dead and you find out in three months |
| **M1b** Chunk storage | 32,768 voxels per chunk, thousands of chunks, in ~8 KB each | Memory blows past 300 MB, or the mesher's inner loop is slow |
| **M1c** Greedy mesher | Padded 34³ array → minimum quad set | Silent geometry corruption — holes, overlaps, missing faces |
| **M1d** Godot bridge | Mesher output → `ArrayMesh`, first real chunk on screen | Cheap; this one is mostly verification |

M1c is the risky one and may spill into a second session. M1a and M1b are the ones
that are expensive to change later, so they get done carefully and first.

---

## M1a — Identity and the registry

### The problem

Everything the game knows about needs an identity that a mod can extend, that survives
being written to disk, and that still means the same thing when the world is reloaded
with a *different set of mods installed*.

### Three ID spaces, and why there are only two

1. **String ID** — `minercraft:stone`. Authoritative, human-readable, mod-facing,
   stable forever. Appears in JSON, in Lua later, and in the world file header.
2. **Numeric ID** — an `int`, for array indexing in hot loops. This is what chunk
   storage actually holds.

A naive design has a third: a per-run numeric ID assigned in registration order. That is
a trap, because adding a mod renumbers everything and every saved chunk now decodes to
the wrong material.

**The fix: there is one numeric space, and it is owned by the world, not the run.**
On world load, read the saved `stringID → numericID` table and seed the registry's
assignment from it. Only content *not* already in the table gets a fresh number,
appended. A new world starts with an empty table and assigns in registration order.
Chunk data therefore never needs remapping, ever.

### The case that matters: a mod is removed

The world still contains that mod's blocks. Rules:

- **Never renumber, never compact.** A removed mod leaves a hole in the table and the
  hole stays.
- The ID keeps resolving, to an *unknown material* placeholder that **retains the
  original string ID**. It renders as something obvious and is inert.
- Re-installing the mod restores everything exactly, because the number never moved.

This is the thing Minecraft learned the hard way and it costs almost nothing to get
right up front.

### Air is special

`minercraft:air` is numeric ID **0**, guaranteed, hardcoded. Both the uniform-chunk fast
path and the mesher's emptiness test depend on "zero means nothing here", and a
zero-initialised array being a valid empty chunk is worth the one special case.

### Shape

- `Identifier` — readonly struct, namespace + path, validated: lowercase,
  `[a-z0-9_.-]`, exactly one colon. Validation is pure and testable.
- `Registry<T>` — `Register`, `Get(Identifier)`, `Get(int)`, `TryGet`. **Frozen after
  the load phase**; mutating a frozen registry throws, which catches load-order bugs
  at the moment they happen instead of three systems downstream.
- `MaterialDefinition` — the first content type. Hardness, support distance,
  permeability, density, texture layer, and flags (opaque, fluid, replaceable).
  Permeability and support distance are needed by M8 and M9 and cost nothing to carry
  now.
- Loading: scan `mods/*/`, each with a `mod.json` manifest (id, name, version,
  dependencies) plus `materials/*.json`.

### The dogfood rule, made concrete

The base game ships as `mods/minercraft/`, loaded through the **identical** loader with
no privileged path. If stone cannot be defined in JSON like a mod would define it, the
format is wrong and we find out immediately rather than at M12.

### Deferred deliberately

Dependency declarations are parsed and validated (error on a missing dependency) but
**not** topologically sorted — base game first, then alphabetical. Real load-order
resolution lands with scripting at M12, when mods can actually conflict.

### Tests

Identifier validation and rejection; registry freeze behaviour; **ID stability across a
simulated save → remove mod → reload → re-add mod cycle**; unknown-material placeholder
retains its string ID; JSON round-trip; air is always 0.

---

## M1b — Chunk storage

### The problem

32,768 voxels per chunk. Thousands of chunks resident. Must be small in memory, fast in
the mesher's inner loop, fast to write on a pick swing, and cheap to serialise. Raw
`ushort[]` is 64 KB per chunk and ~320 MB for a modest view distance, which is why
compression is a requirement here and not an optimisation.

### Three representations behind one interface

1. **Uniform** — the entire chunk is one material. Stores a single int. This covers the
   overwhelming majority of chunks: everything above the surface is air, everything well
   below it is stone.
2. **Palettised + bit-packed** — a small palette of numeric IDs present in this chunk,
   plus an index array at `ceil(log2(paletteCount))` bits per voxel, widening
   1 → 2 → 4 → 8 → 16 as the palette grows. The common case is ≤16 materials, so 4 bits
   per voxel, ~16 KB, and most real chunks land well under that.
3. **Direct** — raw `ushort` per voxel once the palette exceeds 256 entries. A safety
   valve that should almost never trigger.

Note this palette is **per-chunk and unstable** — purely a compression device, rebuilt
freely. It is a different thing from M1a's per-world table, which is stable and saved.
Keeping the two clearly named matters; conflating them is an easy and nasty bug.

### Bit-packing: pad to word boundaries, do not straddle

Packing into `ulong[]`, an index can either straddle two words (denser) or be padded so
`64 / bits` entries fit per word with a few bits wasted. **Pad.** At 4 bits it wastes
nothing; at 5 bits it wastes 4 bits per 64. In exchange every read is a single word
load, shift, and mask — no branch, no second load. Minecraft moved away from straddling
for exactly this reason, and this array is read tens of millions of times per second by
the mesher.

### Promotion and demotion

- Writing a material not in the palette grows the palette, and crossing a power-of-two
  boundary triggers a repack to the wider bit width.
- Shrinking is **never eager** — detecting that a material has left the chunk requires a
  full scan, and a pick swing must not pay for that. Compaction happens opportunistically
  during serialisation, or on an explicit `Compact()`.
- A chunk that becomes single-material demotes back to Uniform at the same moment.

### Threading

Chunk storage is written by the sim thread and read by mesh jobs. Rather than lock, the
sim thread takes the **padded 34³ copy** when it queues the job, so the mesher touches
no shared state at all. Storage therefore needs no synchronisation of its own — but the
copy must happen on the sim thread, and that is an architectural rule, not an
implementation detail.

### Tests

Set/get round-trip at every bit width; palette growth exactly at the 1→2, 2→4, 4→8,
8→16 and direct-fallback boundaries; uniform promotion and demotion; memory assertions
(a uniform chunk really is O(1), a 16-material chunk really is 4 bits/voxel); and the
important one — **a fuzz test running random operations against a naive `ushort[]`
reference implementation**. Bit-packing bugs are invisible to inspection and obvious to
a differential fuzzer.

---

## M1c — Greedy mesher

### The problem

Turn a padded 34³ material array into the minimum set of quads. This is the part of the
engine that most determines whether 0.25 m voxels are viable, and the part where bugs
are silent.

### Algorithm

The standard sweep: for each of 3 axes, each of 2 facing directions, each slice along
that axis — build a 2D mask of exposed faces, then greedily merge rectangles within the
mask. Output is quads, not triangles, until the final emit.

### Design points that are expensive to retrofit

**The merge key.** Two faces merge only if material, light, ambient occlusion, and
normal all match. At M1 light and AO are constant, so it degenerates to material — but
the key must be a struct carrying *all* of those fields from the start, or M5 rewrites
the mesher. This is the same lesson as the vertex format in M0, and it is the single
most important line in this part.

**Padded input.** 34³ means the neighbour test at a chunk border reads real data
instead of needing a cross-chunk lookup, which is what lets mesh jobs run lock-free.

**Output is Godot-free.** A `MeshData` holding plain `float[]` / `int[]` arrays. Keeps
the mesher testable headlessly, and means a dedicated server can link Core without ever
meshing anything.

**Tiled UVs.** A quad spanning 7 voxels gets UV 0..7 and relies on `repeat_enable`,
already set in the M0 shader. This is precisely the thing a texture atlas cannot
express.

**Two surfaces, opaque and transparent**, emitted into separate buffers, because
transparent geometry needs back-to-front sorting. Water is M8, but adding the surface
split later is a refactor of every emit path, so it exists now.

**Allocation-free in steady state.** `ArrayPool<T>` for the working mask and the output
buffers. Frame hitches while mining are the symptom this prevents.

### Tests — a mesher is far more testable than it looks

| Input | Expected |
|---|---|
| Empty chunk | 0 quads |
| Uniform solid chunk | exactly 6 quads |
| Single voxel | exactly 6 quads |
| 3D checkerboard | worst case, zero merging, exactly the culled-mesher count |

Plus two invariants that catch nearly everything:

- **Surface area conservation.** Total merged quad area must equal the culled face
  count. Greedy is only allowed to *merge* — never to lose or duplicate a face.
- **No overlap.** No two quads within a slice may overlap. This is the classic greedy
  bug and it is invisible on screen until it isn't.

Then fuzz random chunks against both invariants, with a naive culled mesher as the
reference. Every one of these runs headless in Core.

---

## M1d — Godot bridge and the first real chunk

Small, and mostly verification.

- `MeshData` → `ArrayMesh`. The single place Godot types meet mesher output.
- Render one real chunk from a generated test pattern — a hill, a sphere, a checkerboard
  — chosen so the merging is visible.
- Visual check, then measure: quad count versus culled, mesh build time, bytes per chunk.
- The `RenderingServer` vs `MeshInstance3D` question stays open here; it needs M2's real
  chunk counts to answer honestly, and one chunk proves nothing.

---

## Order and rationale

**M1a → M1b → M1c → M1d.** Storage needs material IDs, so identity comes first; the
mesher needs something to read, so storage comes second. M1a and M1b are the two that
are painful to change once code sits on them, which is another reason to take them
first and slowly.
