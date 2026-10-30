<#
.SYNOPSIS
    Renders every settings page and setup-wizard step to PNG, without MusicBee.

.DESCRIPTION
    Runs the explicit UiRenderTests in the C# test suite, once per scale. They
    build the windows off-screen against a host whose core never starts, so live
    values (addresses, cache counts, update status) read as unavailable. A page
    that scrolls at any scale fails the run.

    Build the solution first (build-msbuild.ps1 or build.ps1) in the same
    configuration; this script does not build.

.PARAMETER Configuration
    Which test build to run: Release (default) or Debug.

.PARAMETER Scale
    Font scales to render at. 1.5 drives the same autoscaling a 150% display
    does, but fonts derived from the base font do not grow, so check headings
    on a real display before trusting them.

.PARAMETER OutDir
    Where the PNGs go. Emptied first.

.EXAMPLE
    .\tools\render-ui.ps1
    .\tools\render-ui.ps1 -Scale 1, 1.25, 1.5
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [double[]]$Scale = @(1.0, 1.5),

    [string]$OutDir = (Join-Path $PSScriptRoot '..\build\ui-renders')
)

$ErrorActionPreference = 'Stop'

$runner = Join-Path $PSScriptRoot "..\build\bin\tests\$Configuration\net48\MusicBeeRemote.Core.Tests.exe"
if (-not (Test-Path $runner)) {
    throw "No $Configuration test build at $runner. Run .\build-msbuild.ps1 -Configuration $Configuration first."
}

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$failed = $false
try {
    $env:MBRC_RENDER_DIR = $OutDir
    foreach ($s in $Scale) {
        $env:MBRC_RENDER_SCALE = $s.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        Write-Host "==> Rendering at scale $s"
        & $runner -explicit only -class MusicBeeRemote.Core.Tests.Settings.UiRenderTests
        if ($LASTEXITCODE -ne 0) { $failed = $true }
    }
}
finally {
    Remove-Item Env:MBRC_RENDER_DIR, Env:MBRC_RENDER_SCALE -ErrorAction SilentlyContinue
}

Remove-Item (Join-Path $OutDir 'storage') -Recurse -Force -ErrorAction SilentlyContinue
$count = (Get-ChildItem $OutDir -Filter *.png).Count
Write-Host "$count image(s) in $OutDir"
if ($failed) { throw 'A render test failed; see the output above.' }
