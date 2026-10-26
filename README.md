# Overburden

A voxel mining game where you are a mining operation fighting water and gravity.

Rain fills basins. Water finds your excavation. Saturated rock loses cohesion and the
ceiling comes down. You build pumps and shoring to fight back, which needs power,
which needs more mining.

No mobs, no combat. The mine is the antagonist.

## Download

**[The alpha's releases](https://github.com/MatthewCarven/OverBurden/releases)**, newest at the top, for 64-bit
Windows: download the zip under the newest one's "Assets", extract it anywhere and run `Overburden.exe`. It's an
early alpha: far from complete, and things will change.

Older builds, on Google Drive:
- [0.1.1-alpha](https://drive.google.com/file/d/13kcW2xIEVM2Ur8eQSQurquE_8klUd-_H/view?usp=sharing) (the file is
  named 0.1.0; 0.1.1 replaced it under the same name)
- [0.1.2-alpha](https://drive.google.com/file/d/1oJgrgA22TJYgzaOf3IuclViGCbqv1AKw/view?usp=sharing)

## What it runs on

It needs 64-bit Windows and a graphics card that supports Vulkan. Made and played on an Intel Core i9-11900H with an
NVIDIA GeForce RTX 3070 Laptop GPU (8 GB) and 16 GB of RAM. An earlier alpha has also run without errors on an older
desktop: an Intel Core i7-3770 with a GeForce GTX 1660 (6 GB) and 16 GB. Both run Windows 11, and they're all it has
been tried on.

## Playing

The download's `README.txt` ([tools/alpha/README.txt](tools/alpha/README.txt)) gets you started, and
[QUICKSTART.md](QUICKSTART.md) has every key; in the game, F1 shows them and Esc opens the menu.

## Found a bug?

Please [open an issue](https://github.com/MatthewCarven/OverBurden/issues): say what happened, attach the log
(`%APPDATA%\Overburden Alpha\logs\godot.log`; paste the folder into File Explorer's address bar), and give the version
shown in the game's top-left corner.

## Building from source

Windows only for now. You need:
- **Godot 4.7.1-stable, the .NET edition** ("mono"), unzipped into this folder so that
  `Godot_v4.7.1-stable_mono_win64\` sits beside `start.cmd` (the folder is gitignored);
- **a .NET 8 SDK or newer.**

Once, so Godot imports the project:

```
Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --import
```

Then the tests, and the game:

```
dotnet test Overburden.slnx
start.cmd
```

**`start.cmd` is Matthew's own launcher.** It builds the game first, then opens his test world, `LakeTest2`
(created the first time), with the developer keys on (`--dev`, the last table in QUICKSTART.md).
`start.cmd "--world=<name>"` opens or creates another world instead, since a later flag wins. A build from source
plays no music (`media/` isn't in the repository) and saves under `%APPDATA%\Godot\app_userdata\Overburden\`.

## How it's made

Godot 4.7 and C# on .NET 8. The simulation is `Overburden.Core`, which never references Godot; `Overburden.Game` draws
it and takes your input.
- **0.25 m voxels** in 32³ chunks, streamed around you on worker threads. Mining carves material out of the world,
  and the inventory is volumetric: litres of stone, not block counts.
- **Water in three tiers**: lakes and rain as basins, each with one level; a volume-conserving cellular automaton
  for water on the move; and seepage through the rock. Every litre is accounted for.
- **The roof**: rock carries a support field, water weakens it, and an over-dug roof comes down after a 10–20 s
  fuse, in pieces.
- **Machines and power**: relay pumps and outlets on channels, vacuum cells, solar panels, battery packs you wear,
  and quantum tools that spend their charge.
- **Moddable**: every material, tool and machine loads from `mods/` as JSON, and the base game is itself a mod,
  loaded the same way a third party's would be.

The record of how it got here:
- [docs/DESIGN.md](docs/DESIGN.md): the design, its locked decisions, and the alternatives rejected and why.
- [docs/](docs/): a plan for each milestone and system, written before it was built.
- [WORKLOG.md](WORKLOG.md): what happened and why, newest first.
- [TODO.md](TODO.md): the open work.
- [CLAUDE.md](CLAUDE.md): the AI assistant's orientation file, which it reads at the start of every session.

## Contributing

Welcome. [CONTRIBUTING.md](CONTRIBUTING.md) has Matthew's rule, the four checks a change passes, and the contributor
agreement ([CLA.md](CLA.md)) you agree to with a tick in the pull request.

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE.md) — source-available, not open source.

Read it, fork it, mod it, learn from it, share it. Don't sell it.(Please I am trying
to) Personal, hobby, research, educational and charitable use are all explicitly
permitted; commercial use is not.

Chosen because it is the low-regret option while the copyright is held by one person:
relaxing a licence later is one commit, but code once published under a permissive
licence can never be pulled back. If this never becomes commercial, it can move to MIT
at any time.

## Built with

Designed and built by Matthew Carven with [Claude Code](https://claude.com/claude-code) as co-author: the commits'
trailers name Claude Opus 5, Claude Opus 5.5 and Claude Fable 5.1. See [CONTRIBUTORS.md](CONTRIBUTORS.md).
