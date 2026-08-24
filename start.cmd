@echo off
setlocal
rem Launch MinerCraft in the pinned Godot 4.7.1 mono editor build.
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

"%EXE%" --path MinerCraft.Game %*
pause
