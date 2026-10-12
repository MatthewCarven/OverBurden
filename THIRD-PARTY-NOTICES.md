# Third-party notices

Overburden is copyright 2026 Matthew Carven, under the PolyForm Noncommercial License 1.0.0
([LICENSE.md](LICENSE.md)). The game you download also contains software written by others, listed here
with their licences. **The full licence texts are in the `licenses` folder** beside this file in the
download (`tools/alpha/licenses/` in the source).

| Component | What it does in Overburden | Licence | Copyright, and where the full text is |
|---|---|---|---|
| **Godot Engine** 4.7.1 | the game engine: `Overburden.exe` and `Overburden.pck` | MIT | Copyright (c) 2014-present Godot Engine contributors; copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur. `licenses/godot-engine.txt`, which also lists every library the engine bundles, each with its own licence in `licenses/license-*.txt` |
| **GodotSharp** | Godot's C# layer | MIT | part of Godot, as above |
| **.NET runtime** 8.0.31 | runs the game's C# code (the `data_Overburden.Game_windows_x86_64` folder) | MIT | Copyright (c) .NET Foundation and Contributors. `licenses/dotnet-runtime-LICENSE.txt`, and the runtime's own third-party notices in `licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt` |
| **Microsoft.Data.Sqlite** 10.0.11 | reads and writes the save files | MIT | Copyright (c) Microsoft Corporation. The MIT text is `licenses/license-Expat.txt` (Expat is the MIT licence's other name) |
| **SQLitePCLRaw** 2.1.12 (core, provider and bundle) | connects .NET to SQLite | Apache-2.0 | Copyright 2014-2024 SourceGear, LLC. `licenses/license-Apache-2.0.txt` |
| **SQLite** (`e_sqlite3.dll`) | the database inside each save file | public domain | SQLite's authors have dedicated it to the public domain; no licence applies |

## The music

`media/Korobeiniki.mp3`, played while a roof comes down: **Korobeiniki** (a Russian folk song, public
domain), arranged and recorded by Matthew Carven, under the game's own licence.

## How these were gathered

`tools/alpha/godot_licenses.gd` asks the running Godot engine for its licence, its components and their
licence texts, and writes them to `tools/alpha/licenses/`; the two .NET files are copied from the .NET 8
runtime package the export bundles. `tools/make-alpha.ps1` copies the folder into every zip. Rerun the
Godot script when the pinned Godot version changes.
