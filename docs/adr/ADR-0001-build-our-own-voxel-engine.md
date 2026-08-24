# ADR-0001: Build our own voxel engine rather than adopt godot_voxel

**Date:** 2026-08-25
**Status:** Accepted

## Decision

Build MinerCraft's voxel storage, meshing and streaming ourselves, and use
[Zylann/godot_voxel](https://github.com/Zylann/godot_voxel) as a **reference** — read its
approaches, do not take the dependency.

## Context

godot_voxel is a mature, MIT-licensed voxel engine for Godot 4 that already provides
most of Phase 1: chunked mesh generation, Transvoxel LOD, data streaming, saving, terrain
generation, blocky and smooth modes, and physics integration. It has mono builds in CI.
Adopting it would remove M1c, M2, most of M13 and part of M5 — roughly six to ten
sessions of work — and would do them better than we would.

The question came up at the right moment: after M1a, almost nothing was sunk cost either
way. The content registry is independent of voxel storage and survives regardless; the
coordinate types largely survive; only the ~200-line block mesh builder was throwaway.
That will stop being true once M1c lands, which is why it was worth deciding now.

## Alternatives rejected

### Adopt godot_voxel

Rejected, for four reasons in descending weight.

**It would close the headless Core seam.** `MinerCraft.Core` deliberately has no Godot
reference — that is the "maybe someday multiplayer" decision from the design session, and
the test project exists to enforce it. godot_voxel puts world storage inside the engine.
Adopting it reverses an architectural decision rather than merely adding a library.

**The game is the simulation, not the terrain.** Phase 2 — three-tier fluid, groundwater
saturation, support-propagation BFS — needs fast direct access to voxel arrays plus its
own parallel per-voxel fields. godot_voxel has spare data channels so it is not
impossible, but every flood-fill step would cross the C#↔C++ marshalling boundary. A
library is worth adopting when it owns something peripheral to the product; this would
own the thing the product is made of.

**It is a C++ engine module and GDExtension support is incomplete** (still listed on
their roadmap as of 2026-08-25). Realistically that means compiling a custom Godot: SCons,
MSVC, a bespoke editor binary, pinned to whichever engine version we track — instead of
the stock 4.7.1 mono build that works today. Heavy operational weight for a solo project.

**Our design diverges from what it optimises for.** Registry-driven modded materials with
world-stable numeric IDs versus its block library; volumetric inventory; brush placement;
sub-voxel sphere carving as the primary verb rather than block replacement.

### Adopt it now and migrate away later

Rejected. The migration would land exactly when Phase 2 is being built, which is the
worst possible time and the highest-risk part of the project.

## Consequences

**Accepted cost.** We are deliberately spending six to ten sessions rebuilding something
that already exists and is better tested. This is a real cost, not a free choice. The
justification is that Phase 1 is the *known* part of the project — the risk lives in
Phase 2, where the library helps not at all — and that time spent on terrain we fully
understand beats time spent fighting a C++ boundary in the systems that matter.

**We give up Transvoxel LOD for free.** M13 is the piece we would least enjoy writing
from scratch.

**We keep** the headless Core, the stock Godot editor, direct C# access to voxel data for
the fluid and structural systems, and full freedom in the storage format.

**Licensing.** godot_voxel is MIT. Reading it and reimplementing an approach is fine;
lifting code verbatim requires carrying the license notice. If any of its source is
vendored into this repo rather than kept alongside it, that obligation applies — see
`reference/` in `.gitignore`, which keeps it out of the repo by default.

**Deliberately go and read it before M2.** Threaded mesh scheduling and chunk streaming
policy are where we are most likely to make an avoidable mistake, and those are solved
and written up there.

## Revisit triggers

Named now, so the decision gets re-examined on evidence rather than on mood:

- **M1c or M2 exceeds roughly three sessions each.** That would mean we misjudged how
  well-trodden Phase 1 really is.
- **M13 (distance LOD).** The natural second checkpoint — the one piece where the
  library's advantage is largest and our need is most isolated from Core.
- **A decision to make multiplayer real rather than possible** would, counter-intuitively,
  strengthen this ADR rather than weaken it.
