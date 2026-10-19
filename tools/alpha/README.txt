OVERBURDEN - alpha
==================

  "It's far from complete, but I've enjoyed the alpha and beta testing and thought you might like a go
   too."  - Matthew

You run a mine, and the mine is the enemy. Rain fills the low ground, water finds whatever you dig, wet
rock weakens, and an over-dug roof comes down. You fight back with pumps, power and what you build.
No monsters. Things will change, break and get better.


PLAYING
-------
1. Extract this zip anywhere. It makes one folder; keep everything in it together.
2. Double-click Overburden.exe.
   Windows may say "Windows protected your PC", because the game isn't signed. Click "More info", then
   "Run anyway".
3. You need Windows 10 or 11 (64-bit) and a graphics card that supports Vulkan.

Every key is on F1 in the game, and in QUICKSTART.md beside this file. The short version:

  W A S D, mouse      move and look            F         walk / fly
  Left mouse          dig                      T         next tool
  Right mouse         build                    B         brush shape
  1-9, V              choose what to build     Hold Q    keep digging
  V held + 1-9        put what's in hand on that hotbar slot
  P                   a kit of everything      Alt+P     charge your pack and power cells
  F10                 see further              F11       fullscreen
  F12                 the map                  Ctrl+Z    undo
  Esc                 menu: resume, quit       F1        every key
  Quitting saves the world, from the menu or by closing the window.

Your bare hands dig dirt, not stone. Press P, wear a battery pack (V until it's in hand, right-click), press
Alt+P to charge it, then T to the quantum miner: that digs anything.


YOUR WORLDS AND THE LOG
-----------------------
Everything the game saves is in %APPDATA%\Overburden Alpha\ (paste that into File Explorer's address bar).
You start in the world in worlds\default\. The log is logs\godot.log.
Worlds move forward, not back: once a newer alpha has opened a world, an older one won't open it again
(0.1.3 saves the hotbar, which 0.1.2 can't read).


FOUND A BUG?
------------
Please open an issue at https://github.com/MatthewCarven/OverBurden/issues : say what happened, attach
logs\godot.log, and give the version shown in the game's top-left corner.


LICENCE
-------
Free to play and share, not to sell: PolyForm Noncommercial 1.0.0 (LICENSE.md).
The game includes software by others (Godot, .NET, SQLite): THIRD-PARTY-NOTICES.md and the licenses folder.
Music: Korobeiniki (Russian folk song, public domain), arranged by Matthew Carven.
