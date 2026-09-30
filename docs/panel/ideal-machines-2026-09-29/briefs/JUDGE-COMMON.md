# Common instructions for every judge on the ideal-machines panel

You are one of **three judges** on a design panel for Overburden, a Godot 4 + C# voxel mining game (repo
`C:\Users\Admin\Desktop\-=Programming=-\MinerCraft`). Four designers each wrote a full design for "the ideal
machines" from one angle: 1 engine cost, 2 gameplay and the demo, 3 the mod format, 4 physical
plausibility. **You score all four through one lens only**, the one named in your task. A synthesis will
be built from the winner plus grafts from the others, so your job is to find what is wrong and what is
best, with evidence.

`<scratchpad>` = `C:\Users\Admin\AppData\Local\Temp\claude\C--Users-Admin-Desktop---Programming---MinerCraft\0dd9b260-cc4b-4515-9e45-424b25b5a28c\scratchpad`

## Read

1. `docs/IDEAL-MACHINES-BRIEF.md` in the repo: Matthew's words verbatim and **nine taken decisions** that
   are his (all six extras; inheritance in JSON; the creative demo first; one pool with bankruptcy a
   feature; the scope; automated quarries; materials; water in the inventory at the last tech through a
   tool or machine made for it; the quarry works only where chunks are loaded, for OH&S and for servers,
   with a per-player machine cap as a revisit trigger only).
2. `<scratchpad>\ideal-machines\DESIGNER-COMMON.md`: the numbered constraints every designer was given,
   corrected against the code.
3. The four designs: `<scratchpad>\ideal-machines\designs\1-engine.md`, `2-gameplay.md`, `3-format.md`,
   `4-physics.md`.
4. The five reader maps in `<scratchpad>\ideal-machines\readers\` (A engine, B plans, C store, D creative,
   E materials) and the repo itself, **to check claims**. A design's claim about the code is a hypothesis
   until you've checked it. Read-only: do not edit, build, run Godot or run tests.

## Deliver

Write your verdict to the file named in your task, 3,000–5,000 words, in this order:

1. **Scores**: each design 1–10 through your lens, one paragraph each saying why.
2. **Defects, per design**: numbered, most severe first. Each names the section, the claim, why it is
   wrong or costly, and the evidence (a `file:line`, a reader map's section, or arithmetic shown). Mark
   each **must not ship** (it breaks a constraint, an identity, a taken decision or a non-negotiable) or
   **cost** (it works but is dearer than it needs to be).
3. **Where the designs disagree and your lens decides it.** For each real fork (for example: does mining
   by hand credit the pocket or the vault; is the vault's power bill flat or by fill; one carried item or
   two; grades on the vault ID or on parts; strict loader or not; a template world or a kit; recall at
   release or at the drain; does a signal ever dump a vault), say which arm your lens prefers and why.
4. **The winner through your lens, and the grafts**: the best idea in each other design worth taking.
5. **Estimates stated as facts**: any figure a design presents as measured or settled that is not, with
   what it should say.

Be adversarial. Past panels found designs that were each self-consistent and wrong: a pump that pumped its
own outlet back with the identity holding perfectly; a coal figure 15× out by its own arithmetic; three
designs asserting atomicity the store didn't have. Look for that class of error.

Return to the coordinator only the path you wrote and a summary of at most 150 words: your winner, and
your three most severe must-not-ship findings.
