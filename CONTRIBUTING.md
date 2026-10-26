# Contributing to Overburden

## Matthew's rule

> *"if you have to modify core do it in a way that everyone else can take advantage of it too"*
> — Matthew Carven, 2026-10-12

Everything below is that rule made checkable.

## The four checks

Every pull request passes these, and [the pull request template](.github/pull_request_template.md) asks for each.

1. **Content arrives as a mod, through the loader the base game uses.** A new material, tool or machine is JSON
   under `mods/<your-mod>/`, loaded the way `Overburden.Game/mods/overburden/` (the base game, itself a mod) is.
   There is no privileged path for the base game, and a change must not add one. If something can't be said the way
   a mod would say it, the format is what needs changing, for everyone.
2. **`Overburden.Core` never references Godot, and a Core change is a feature any mod can use**, never a special
   case for one material, one machine or one mod. Core's independence from the engine is the seam multiplayer will
   use; `Overburden.Core.Tests` stops compiling if Core reaches for Godot.
3. **Every change comes with tests, and the suite passes:** `dotnet test Overburden.slnx`. A rule nobody has watched
   fail isn't tested yet, so break your rule on purpose once and see a test catch it.
4. **Water and energy stay exact.** Neither is ever created or destroyed by rounding, and each is held by a named
   identity that the tests check under load:
   - **Water**, Tier A (lakes and rain): `held == rained + injected + received − spilled − drawn`
     (`Overburden.Core/Hydrology/WaterSystem.cs`), and the two tiers report their seam from both ends, so Tier B's
     `drawn` and `handed` meet Tier A's to the litre. Held by
     `FluidJobTests.MutationsLandingUnderARunningWorkerLeaveEveryIdentityExact`, among others.
   - **Energy**: the power pool with the worn pack inside it, every term a `long` in millijoules
     (`Overburden.Core/Machines/EquippedPack.cs`), and the machines' own line, `pumped == emitted + vaulted` per
     pump per boundary (`Overburden.Core/Machines/MachineTick.cs`). Both held by
     `MachineTickTests.ThePoolIdentityHoldsOverAThousandBoundariesWithEverythingRunning`.
   - **The vault store**: `held + owed == entering − leaving + deposited − withdrawn`
     (`Overburden.Core/Machines/MachineSet.Vaults.cs`), held by the vault tests.

   A new way into or out of one of these quantities is a new term in its identity, printed and tested, or the
   identity stops meaning anything.

## The practical rules

- **Content IDs are namespaced strings** (`overburden:stone`, `yourmod:copper_ore`), resolved to numbers through a
  table each world owns and saves. Never an enum; never renumber or compact the table (a removed mod leaves a hole
  that stays). `overburden:air` is always number 0. And `minercraft:`, the base game's namespace before the rename,
  is an alias of `overburden:` forever: worlds saved before 2026-09-03 contain it.
- **The other settled rules** are in [CLAUDE.md](CLAUDE.md#non-negotiables) and
  [docs/DESIGN.md](docs/DESIGN.md#non-negotiables-decided-week-one-murder-to-change-later), with the reasons: textures
  are a texture array, never an atlas (greedy meshing tiles UVs past 0..1); chunk storage is `[z][x][y]`, Y
  contiguous; light and ambient occlusion stay in the mesher's merge key. Read the matching plan in [docs/](docs/)
  before changing a system, and DESIGN.md's rejected alternatives before proposing one.
- **Open an issue first** for anything that touches `Overburden.Core` or a save format (the world file's header, its
  tables, a chunk's bytes), so the shape can be agreed before the work. A save format change also needs a test that
  opens a world written by the version before it.
- **Benchmarks run in Release only**: `dotnet run --project Overburden.Bench -c Release -- --light` (every arm is in
  [docs/COMMANDS.md](docs/COMMANDS.md)). Debug is 3–10× slower and its numbers look just as real.
- **Line endings**: the repository stores LF, and `.gitattributes` makes git convert for you, whatever your editor
  writes.
- **Writing a mod**: a page of its own is still to come. Until then, the loader's rules are in
  [DESIGN.md's "Mod support"](docs/DESIGN.md#mod-support) (strict files: an unknown or repeated key is refused,
  naming the file and the nearest key; `mod.json` declares its `format`), and the base game's own files in
  `Overburden.Game/mods/overburden/` are the worked example.

## The contributor agreement

Every contribution comes under **[CLA.md](CLA.md)**, Harmony's individual contributor licence agreement, filled in
for this project. You keep the copyright in what you write; you license it to Matthew Carven, who may license it under
any terms, including commercial ones, as long as it also stays available under the licence the project used when you
submitted it (today PolyForm Noncommercial 1.0.0). There is a patent licence too. Read it before your first pull
request. It is a template, not legal advice.

**Why it's needed:** without an agreement, GitHub's terms bring a contribution in under the repository's own licence,
and PolyForm Noncommercial gives no right to sublicense. Matthew could then use your code only as anyone may, and a
commercial edition or any change of licence would need every contributor's consent.

### How you agree

Tick the box *"I have read CLA.md and agree to it for this contribution"* in the pull request's description (the
template puts it there). The pull request is the record. Nothing unticked is merged.

### Work that isn't all yours

The agreement covers what you wrote yourself. If a pull request includes anything you didn't write, or don't own
outright (code copied from elsewhere, a texture, a sound), say so in the pull request: what it is, where it came
from, and the licence it's under. Your tick covers your own work; those words are the record for the rest.

If you are employed, the agreement asks you to have your employer approve it first, since an employer can own what
you write (its section 3(c)). A company that wants to contribute as itself can ask for the entity version.
