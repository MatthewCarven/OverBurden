@echo off
setlocal
rem Launch Overburden in the pinned Godot 4.7.1 mono editor build.
rem Prefers a copy vendored beside this repo; falls back to the shared one in GAME/.

set "EXE=Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
if not exist "%EXE%" set "EXE=..\GAME\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"

if not exist "%EXE%" (
  echo.
  echo Godot 4.7.1-stable mono not found.
  echo Expected either:
  echo   .\Godot_v4.7.1-stable_mono_win64\
  echo   ..\GAME\Godot_v4.7.1-stable_mono_win64\
  echo.
  pause
  exit /b 1
)

rem Everything after the bare "--" is a game flag; everything you pass to start.cmd lands there
rem too, and a later --lightbudget= wins. The 16 ms light budget is the play setting for the
rem Debug build this launches: a Debug derive is ~1.4 ms and the 4 ms default gives two or three
rem a frame, which lit a mined-out room over tens of seconds on 2026-09-03. The default stays 4 ms
rem because every recorded measurement was taken at it; this launcher is a play launcher.
rem --adaptive=1 is the adaptive view radius (docs/ADAPTIVE-RADIUS-PLAN.md): the box falls to the 27-chunk
rem floor while flying with a derive backlog and grows back a ring at a time while you hang around. Off
rem by default for the same reason the light budget is: every recorded figure was taken at a fixed radius.
rem --spread=counted was passed here from 2026-09-07 to 2026-09-09 for the counted divisor's play; the A/B
rem on flat grass read the same under both rules ("they both look like water"), so the default, fixed,
rem stands and the flag is gone. Pass --spread=counted yourself to see the other arm.
rem --fluidnear=2 is the water thread's first slice (docs/WATER-THREAD-PLAN.md): the 5x5 chunk
rem column around you is visited every Tier B tick and water further off is deferred past the
rem boundary under a quota. Off by default for the reason the other two are: every recorded figure
rem was taken without it. --fluidnear=0 is the control arm, --fluidfar=<cells> the floor's sweep.
"%EXE%" --path Overburden.Game -- --lightbudget=16 --adaptive=1 --fluidnear=2 %*
pause
