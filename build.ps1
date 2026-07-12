# Unified build for MusicBee Remote: the Rust core (mbrc_core.dll, x86) and the
# C# plugin (mb_remote.dll). By default builds both; pass -Rust or -Plugin to
# build just one.
#
#   .\build.ps1                      # both, Release
#   .\build.ps1 -Configuration Debug # both, Debug (copies to app\MusicBee\Plugins)
#   .\build.ps1 -Rust                # just the Rust core
#   .\build.ps1 -Plugin              # just the C# plugin
#   .\build.ps1 -Clean               # remove build output first
#
# The Rust core is built for i686-pc-windows-msvc (the plugin is x86/net48) with
# the `plugin` profile (panic = "unwind" so a Rust panic can't abort MusicBee).
param(
    [ValidateSet("Debug", "Release")] [string]$Configuration = "Release",
    [switch]$Rust,
    [switch]$Plugin,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$target = "i686-pc-windows-msvc"

# No selector => build everything; a selector limits to that component.
$buildRust = $Rust -or (-not $Rust -and -not $Plugin)
$buildPlugin = $Plugin -or (-not $Rust -and -not $Plugin)

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

if ($Clean -and (Test-Path "$root\build")) {
    Write-Step "Cleaning build output"
    Remove-Item "$root\build" -Recurse -Force
}

# ---------------------------------------------------------------- Rust core ---
if ($buildRust) {
    Write-Step "Building Rust core (mbrc-core, $target, $Configuration)"

    if (-not (rustup target list --installed | Select-String -SimpleMatch $target)) {
        Write-Host "Installing rust target $target..." -ForegroundColor Yellow
        rustup target add $target
    }

    # Release -> the size-optimised, unwind `plugin` profile; Debug -> dev.
    if ($Configuration -eq "Release") {
        cargo build -p mbrc-core --target $target --profile plugin
        $profileDir = "plugin"
    }
    else {
        cargo build -p mbrc-core --target $target
        $profileDir = "debug"
    }
    if ($LASTEXITCODE -ne 0) { Write-Error "Rust build failed"; exit $LASTEXITCODE }

    $script:CoreDll = "$root\target\$target\$profileDir\mbrc_core.dll"
    if (-not (Test-Path $script:CoreDll)) { Write-Error "mbrc_core.dll not found at $($script:CoreDll)"; exit 1 }
    Write-Host "Built $($script:CoreDll)" -ForegroundColor Green
}

# ---------------------------------------------------------------- C# plugin ---
if ($buildPlugin) {
    Write-Step "Locating MSBuild"
    $msbuild = $null
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -prerelease -products * `
            -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
    if (-not $msbuild -or -not (Test-Path $msbuild)) {
        $editions = "Community", "Professional", "Enterprise", "BuildTools"
        $candidates = foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
            foreach ($ver in "18", "2022") {
                foreach ($edition in $editions) {
                    "$base\Microsoft Visual Studio\$ver\$edition\MSBuild\Current\Bin\MSBuild.exe"
                }
            }
        }
        $msbuild = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $msbuild) { Write-Error "MSBuild not found. Install Visual Studio 2026 (or 2022) with the MSBuild component."; exit 1 }
    Write-Host "Using MSBuild: $msbuild" -ForegroundColor Green

    Write-Step "Restoring NuGet packages"
    & $msbuild "$root\MBRC.sln" /t:Restore /p:Configuration="$Configuration" /v:M
    if ($LASTEXITCODE -ne 0) { Write-Error "Package restore failed"; exit $LASTEXITCODE }

    Write-Step "Building plugin ($Configuration)"
    & $msbuild "$root\MBRC.sln" /p:Configuration="$Configuration" /p:Platform="Any CPU" /m /v:M /nr:false
    if ($LASTEXITCODE -ne 0) { Write-Error "Plugin build failed"; exit $LASTEXITCODE }
}

# ---------------------------------------------- Stage the native core dll ------
# Place mbrc_core.dll side-by-side with mb_remote.dll wherever the plugin lives.
if ($buildRust -and $script:CoreDll) {
    $dests = @()
    $pluginOut = "$root\build\bin\plugin\$Configuration\net48"
    if (Test-Path $pluginOut) { $dests += $pluginOut }
    if ($Configuration -eq "Debug") {
        $pluginsFolder = "$root\app\MusicBee\Plugins"
        if (Test-Path $pluginsFolder) { $dests += $pluginsFolder }
    }
    foreach ($dest in $dests) {
        Copy-Item $script:CoreDll -Destination $dest -Force
        Write-Host "Staged mbrc_core.dll -> $dest" -ForegroundColor Green
    }
}

Write-Host "`nBuild completed successfully!" -ForegroundColor Green
