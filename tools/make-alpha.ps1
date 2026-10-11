# Overburden: builds the alpha zip (docs/ALPHA-PLAN.md, decision 13). One command, so the next alpha is
# the same zip:
#
#   pwsh tools/make-alpha.ps1
#
# Exports the Windows build through Godot 4.7.1 mono (the "Windows Desktop" preset in
# Overburden.Game/export_presets.cfg), lays out what a player needs beside the exe, and zips it as
# build/Overburden-<version>-win64.zip with one folder inside, so "Extract All" gives a folder and not
# a spill of files. Prints the zip's size, SHA-256 and commit.
#
# Needs the Godot 4.7.1 mono editor (vendored beside the repo or in ../GAME, found as start.cmd finds
# it) and its export templates (in the editor: Editor > Manage Export Templates > Download and
# Install). Close the game first: the export builds the C#, and the editor's run rotates the same
# godot.log a play writes. -Godot <console exe> names the editor when it is somewhere else (a worktree).

param([string]$Godot)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$godot = if ($Godot) { $Godot } else { Join-Path $repo 'Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' }
if (-not (Test-Path $godot)) {
    $godot = Join-Path $repo '..\GAME\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
}
if (-not (Test-Path $godot)) { throw 'Godot 4.7.1-stable mono not found (beside the repo or in ..\GAME, as start.cmd looks).' }
if (Get-Process -Name 'Godot_v4.7.1-stable_mono_win64*' -ErrorAction SilentlyContinue) { throw 'Godot is running: close the game first.' }

[xml]$project = Get-Content (Join-Path $repo 'Overburden.Game\Overburden.Game.csproj')
$version = @($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'No <Version> in Overburden.Game.csproj.' }
$commit = (git -C $repo rev-parse --short HEAD).Trim()
if (git -C $repo status --porcelain --untracked-files=no) {
    Write-Warning "The tree has uncommitted changes: the build is stamped $commit but holds them too."
}

$build = Join-Path $repo 'build'
$stage = Join-Path $build "alpha\Overburden-$version"
if (Test-Path (Join-Path $build 'alpha')) { Remove-Item -Recurse -Force (Join-Path $build 'alpha') }
New-Item -ItemType Directory -Force $stage | Out-Null

$log = Join-Path $build 'export.log'

# No MSBuild worker left behind for reuse: one held Godot's output open after the export finished, and
# the editor waited on it for ten minutes at no CPU (2026-10-12, the first export run from his folder).
$env:MSBUILDDISABLENODEREUSE = '1'
& $godot --path (Join-Path $repo 'Overburden.Game') --headless --export-release 'Windows Desktop' (Join-Path $stage 'Overburden.exe') *> $log
if ($LASTEXITCODE -ne 0) { throw "The export failed ($LASTEXITCODE); see $log" }
if (Select-String -Path $log -Pattern '^ERROR' -CaseSensitive -Quiet) { throw "The export logged errors; see $log" }

# Beside the exe, where an exported build looks (ALPHA-PLAN decisions 7 and 8).
Copy-Item -Recurse (Join-Path $repo 'Overburden.Game\mods') (Join-Path $stage 'mods')
$music = Join-Path $repo 'media\Korobeiniki.mp3'
if (Test-Path $music) {
    New-Item -ItemType Directory -Force (Join-Path $stage 'media') | Out-Null
    Copy-Item $music (Join-Path $stage 'media')
} else {
    Write-Warning 'media\Korobeiniki.mp3 not found: this zip plays no music.'
}

# The papers (ALPHA-PLAN decisions 22 and 23). Each is warned about rather than refused while they are written.
foreach ($paper in 'LICENSE.md', 'QUICKSTART.md', 'THIRD-PARTY-NOTICES.md', 'tools\alpha\README.txt') {
    $path = Join-Path $repo $paper
    if (Test-Path $path) { Copy-Item $path $stage } else { Write-Warning "$paper not found: not in this zip." }
}

$zip = Join-Path $build "Overburden-$version-win64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
'{0}  {1:N1} MB  sha256 {2}  commit {3}' -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB), $hash, $commit
