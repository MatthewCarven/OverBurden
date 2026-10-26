# Contributors

## Matthew Carven — author, designer, owner

Owns the project and every design decision in it. Set the direction, chose the
engine and the voxel scale, cut mobs from the scope, and added the rainfall-driven
flooding that turned three separate features into one mechanic.

## Claude (Claude Opus 5, Claude Opus 5.5 and Claude Fable 5.1, via Claude Code) — co-author

Technical design and implementation. Architecture analysis, systems design, the
written design documents, and code.

Claude commits carry a `Co-Authored-By:` trailer naming the model that made them
(`Claude Opus 5`, `Claude Opus 5.5` or `Claude Fable 5.1`, each `<noreply@anthropic.com>`),
so the collaboration is visible in `git log` and in the contributor graph.

## How the collaboration actually works

Matthew directs and decides; Claude analyses, proposes, and builds. Both halves
show up in the record — [WORKLOG.md](WORKLOG.md) notes where a suggestion came from
Claude and where Matthew overrode or improved it, including the exchange that
produced the core mechanic.

Attribution here is deliberately specific rather than headline-grabbing. Claude is
credited as co-author and not as primary contributor, because the design judgement
is Matthew's, and because AI-generated work sits in unsettled copyright territory —
overstating an AI's authorship would complicate licensing and outside contributions
later.

## Licensing and contributions

The project is under [PolyForm Noncommercial 1.0.0](LICENSE.md). How to contribute is
[CONTRIBUTING.md](CONTRIBUTING.md), and every contribution comes under the contributor
agreement in [CLA.md](CLA.md), agreed with a tick in the pull request.

This matters more than it looks. Matthew holds the entire copyright, so the licence can be
changed at any time by editing one file. Without an agreement, an outside contribution would
arrive under PolyForm Noncommercial itself, which can't be sublicensed, and relicensing would
then need every contributor's consent. The agreement keeps the option open: a contributor
keeps their copyright and licenses the work to Matthew under any terms, while it always stays
available under the licence in use when it was submitted.

The caveat noted above applies here too: material written by Claude sits in unsettled
copyright territory, and a licence can only restrict what is actually protectable. The
durable protection for a game is the name and the content, not copyright over the
engine source.
