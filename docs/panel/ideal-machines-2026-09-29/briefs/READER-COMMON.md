# Common instructions for every reader on the ideal-machines panel

You are a **reader** on a design panel for Overburden, a Godot 4 + C# voxel mining game. Repo:
`C:\Users\Admin\Desktop\-=Programming=-\MinerCraft`.

The panel is planning "the ideal machines". **Read `docs/IDEAL-MACHINES-BRIEF.md` first, whole.** It has
Matthew's words (he is the designer and owner), what he has already decided, the recommendations to test,
the scope, and eight numbered constraints. Then skim the repo's `CLAUDE.md`: its non-negotiables and its
"Things not to rediscover".

Your job is **not to design**. Map what exists, so that four designers who will not read the code can
design against facts rather than guesses. A designer who believes a thing exists that doesn't, or who
re-proposes something already rejected, costs the whole panel.

## Rules

- **Read-only on the repo.** Do not edit, build, run Godot, or run tests. Matthew may be playing, and a
  build touches locked DLLs. Read-only git commands (`log`, `show`, `grep`) are fine.
- Write your map to the file path named in your task. Plain markdown. Aim for 2,500–5,000 words; dense
  beats long. Use headings the designers can scan.
- **Every claim about code carries `file:line`.** Every number says where it came from: a measured figure
  with its doc or worklog source, or "estimate" or "my inference". Say plainly when something exists only
  in a plan and not in the code; that distinction is the most valuable thing you can give.
- Large docs (M10-LADDER-PLAN.md is 240 KB, M8-PLAN 220 KB, TODO-HISTORY 560 KB) should be read by
  section. Grep for headings first.
- End the map with a section **"Traps"**: things a designer who hasn't read the code would get wrong.
- Return to the coordinator only: the path you wrote, and a summary of at most 150 words naming the three
  most design-relevant facts you found.
