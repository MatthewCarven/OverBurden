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

# No build process may outlive the export's own build: one that does inherits Godot's output and the
# editor waits on it at no CPU (2026-10-12, his folder: an MSBuild worker kept for reuse, then, with that
# off, the C# compiler server, which only quits after ten idle minutes; a run took 615 s for 15 s of
# work). It bites when no build server was running before the export started one.
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
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

# The papers (ALPHA-PLAN decisions 22 and 23), and the full licence texts THIRD-PARTY-NOTICES.md points at.
# A missing one refuses the zip: a download without its notices is the thing they exist to prevent.
foreach ($paper in 'LICENSE.md', 'QUICKSTART.md', 'THIRD-PARTY-NOTICES.md', 'tools\alpha\README.txt') {
    $path = Join-Path $repo $paper
    if (-not (Test-Path $path)) { throw "$paper not found: the zip needs it." }
    Copy-Item $path $stage
}
$licenses = Join-Path $repo 'tools\alpha\licenses'
if (-not (Test-Path (Join-Path $licenses 'godot-engine.txt'))) { throw 'tools\alpha\licenses is missing its texts: run tools\alpha\godot_licenses.gd (see its header).' }
Copy-Item -Recurse $licenses (Join-Path $stage 'licenses')

$zip = Join-Path $build "Overburden-$version-win64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
'{0}  {1:N1} MB  sha256 {2}  commit {3}' -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB), $hash, $commit
