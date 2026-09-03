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
"%EXE%" --path Overburden.Game -- --lightbudget=16 %*
pause
