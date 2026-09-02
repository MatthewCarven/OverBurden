# M7 — the play

> Written 2026-09-02, when M7 went code-complete. **This is the only outstanding part of the
> milestone.** Five questions, none of which a test can answer, plus one A/B that is a matter of
> taste rather than correctness.

Run it the ordinary way:

```bash
start.cmd
```

Everything below happens in a normal session with no flags. The flags are only for provoking a
thing you would otherwise have to wait for.

---

## Getting weather without waiting for it

The weather is a pure function of `(seed, world time)`, so a storm can be asked for by name:

| Flag | What it does |
|---|---|
| `--clock=<seconds>` | Starts the run somewhere in this world's forecast. **`--clock=250` opens in a 36 mm/h downpour.** **Any *existing* world overrides it — including the default one you have been playing** — because what that world remembers is the thing under test. So pair it with `--nosave`, or a `--world=` name that does not exist yet, otherwise the flag silently does nothing. |
| `--clockscale=<x>` | Makes world time run faster, so a whole two-minute weather episode fits in a short session. **Speeds the sky up too** — not the same knob as the one below. |
| `--rainscale=<x>` | Multiplies the rain reaching the ground and leaves the sky honest. Ships at **×20** — halved from ×40 by the play, 2026-09-02. |
| `--weather=<0..1>` | Forces the sheen and the overcast without making it rain. A look override for screenshots. |
| `--norain` / `--nowater` | The control arms. Everything else stays; only the particles or only the water go. |

**55% of episodes are dry**, so a session started at t = 0 may see nothing for a couple of minutes.
That is the design, not a fault — the wet term only means something against the quiet before it.

---

## The five questions — **all five answered 2026-09-02; M7 is done**

| | Question | Answer |
|---|---|---|
| 1 | Does rain read as rain? | **Yes**, and no knob was reached for. |
| 2 | Does it stop at the mine entrance? | **Yes** — *the detail people notice*, noticed. |
| 3 | Are splashes missed? | **No** — re-tested knowingly, confirmed absent, and *"it's a cosmetic"*. |
| 4 | Does a pond rising feel right? | **Yes**, and it moved `rainfallScale` ×40 → ×20. |
| 5 | Is the 4 m shoreline tolerable? | **Yes**, explicitly not worth the work. |

The detail of each is below, kept for the reasoning.


### 1. Does rain read as rain, and a storm as weather rather than as the renderer breaking?

Stand outside in a storm (`start.cmd -- --clock=250 --nosave` — see the table: an existing world overrides the clock). The things to judge: streak length and density, whether
the near-fade is enough or you still catch a drop right at the eye, and whether the sky going
leaden reads as cloud or as something being wrong with the picture.

**The knobs if it is off**: `drop_length`, `drop_width`, `alpha` and `near_fade_*` in
`Rendering/RainDrop.gdshader`; `DropCount` and `BoxHalfWidthMetres` in `Rendering/Rain.cs`;
`OvercastDimming` in `Main.cs` for how dark a full storm gets.

### 2. Does it stop at the mine entrance?

**DESIGN calls this *the detail people notice*, and it is the one M7c-2 could not photograph.**
Underground shots correctly show no rain, which is the occlusion working in its negative form —
what is missing is a picture of rain visibly pouring *into* a hole.

Dig a shaft down from open ground, stand beside it in a storm and look in; then stand at the
bottom and look up. The field is built from **edited** sky heights at 0.5 m, so a `hand_pick` bore
(0.75 m, three voxels) should register.

**Revisit trigger, recorded: a shaft rain should fall into and does not.**

### 3. Are splashes missed?

**What a splash is**, since the play asked and the question assumed it: the small impact effect
where a drop lands — a brief puff of droplets or a ring on the surface, so rain visibly *hits*
the ground rather than vanishing into it. A per-impact particle, unrelated to water volume.

**Answered 2026-09-02, twice.** First by silence — never raised across a play that reported on
rain, occlusion, standing water, pacing and the shoreline. But the observer did not have the
concept, so it was re-run knowingly: a storm on open ground, watching where the drops meet it.
*"No, could not see"*, and *"nah like it's a cosmetic."* **Confirmed absent by a primed
observer, and confirmed not to matter** — which is a different and much stronger answer than
the silence was. The four M7c-2 findings go to the cosmetics pass in TODO.

**There are none** — they were cut at M7c-2 after two implementations drew nothing, and the rain
currently lands on dry-looking ground with nothing to mark the impact. If you do not notice, the
four findings in that worklog entry stay on the shelf. If you do, they are where a second attempt
starts, and the most useful of them is that **a `texture()` call inside a particle `start()`
silently produces particles that never appear.**

### 4. Does a pond visibly rising over minutes feel right, or does it feel broken?

**This is the question that judges the project-level principle**, not just this milestone's tuning
— *slower than realtime is accuracy, not approximation*.

It is also where **×40 was confirmed or moved — and it was moved, to ×20**. The number came from two measurements rather than
from feel: the bench says the median basin fills in **32 minutes of this world's heaviest possible
weather** unscaled, and the game says a window reaches **10.2% of leaf capacity in 40 minutes of
ordinary weather** — so a full basin unscaled is about six and a half hours. ×40 made that roughly
ten minutes; the play said *"a little too fast, um like you could half the fill speed, it would
still work as well I reckon"*, so **×20 ships and a basin fills in roughly twenty minutes**. The
arithmetic could say the answer was somewhere in the tens and could never say where inside them —
which is the whole reason this question is a play rather than a bench run.

Sweep it with `--rainscale=10`, `20`, `40` and see which one reads as weather rather than as a tap.
**Judged on the spawn's shallow terraced ponds; a deep valley is the recorded revisit trigger.**

**Find water first.** The spawn's terrain is gently terraced, so its basins are shallow puddles
rather than lakes — walk to a real valley. The `M7d:` log line every five seconds tells you what is
standing, as a percentage of capacity, so you can tell "no water here" from "no water at all".

### 5. Is the 4 m shoreline tolerable until M8?

The surface is emitted on the 4 m analysis grid against 0.25 m terrain, so it **stair-steps at the
water's edge** — the most-looked-at part of a pond. Accepted for M7 because **M8's Tier B replaces
it**; refining shore cells now would be building the thing M8 deletes.

The question is only whether it is *tolerable in the meantime*, not whether it is good.

**Answered 2026-09-02: tolerable.** *"It would be better to fill exactly but I honestly don't
know how to do it and I ain't gonna task you with doing it just to satisfy a base cosmetic."*
The question that came out with it — *"unless I make a hole 4x4 it isn't going to drain into
eventually?"* — is now an M8 planning item, and the answer is that **the 4 m grid is Tier A's
analysis resolution, not water's physics resolution**. A 0.75 m bore drains, provided M8 treats
the breach at voxel resolution rather than re-analysing the watershed at 4 m. See TODO,
*the minimum drainable hole*.

---

## The one A/B, which is taste rather than correctness

M7c-1 found that `face_tint` was reading the **view-space** normal, so the fake directional shading
rotated with the player — the top of a block dimmed as you looked down at it. It is fixed, and
world space ships.

**You had said you liked the old look**, so the old arm is still there:

```bash
start.cmd
```
then compare against a run with `--viewnormal=1`. The two are identical facing the default
direction and diverge once you turn; measured on a riser face, (69,130,64) in both at yaw 0 and
(59,128,54) against (53,116,50) at yaw 40.

---

## What the logs will tell you

Every five seconds:

```
M7c: t 280 s, rain 10/12 (36 mm/h), wet 0.28, cloud 0.46, sky 0.72 | field 3 rebuild(s) (2 moved / 1 dug), 0.96 ms, 0% unknown, ground -3.5..5.0 m, rain falling
M7d: 2,822,142 L standing (25.3% of 11,136,000 L) in 346/346 basin(s), 346 basin(s) banked | window 512 m x1, 40.1 ms to analyse, scale x20 | surface 1009 quad(s) over 346 basin(s), 1.05 ms
```

Two of those are worth knowing how to read:

- **`(2 moved / 1 dug)`** — a *dug* rebuild means breaking through to daylight re-aimed the rain.
  If you dig a shaft and this does not move, that is the wire in question 2 being loose.
- **`0% unknown`** — the share of the occlusion field with no loaded column under it. Rises at
  small view distances and is a profiling artefact rather than a defect; at play distances it
  should be near zero.

---

## What the play has already found — a sixth thing, 2026-09-02

> *"The surface level was rising in sealed ponds like, but where I had tunnelled into the earth, it
> did not fall in."*

**Correct, and recorded before it was seen.** `WaterSurface` sweeps `HeightField`, which is built
from `ISurfaceHeights.SurfaceHeightAt` — analytic, seeded, **unedited**. DESIGN, settled at M7's
planning pass: *"Edits do not change the watershed at M7. Water reaching an excavation is Tier B,
which is M8; the seam is `SkyColumn`, which already exists and already persists."*

An excavation is invisible to water in **both** directions, and only one half looks like a
boundary: water cannot get *in* to a tunnel under a pond, and a cut through a basin's rim does not
drain it *out* either.

**One thing to look for while playing, because it is the shape that reads as broken rather than as
missing:** dig an open pit *inside* a pond's footprint, below the water level. The quad is still
emitted from the unedited height, so the surface should hang in the air over the hole with the
pit's walls drawing through it. **If you see that, it is the one part of this worth a cosmetic
guard before M8. If you do not, nothing here moves.**

No stopgap is proposed. "Water finds your excavation" *is* Tier B, and every cheaper version is
something M8 deletes.
