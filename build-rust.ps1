# Build script for the Rust mbrc-core native library (x86)
param(
    [string]$Configuration = "release"
)

$ErrorActionPreference = "Stop"

# Check that cargo is available
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    Write-Error "cargo not found. Please install Rust: https://rustup.rs/"
    exit 1
}

# Ensure the i686-pc-windows-msvc target is installed
Write-Host "Ensuring i686-pc-windows-msvc target is installed..." -ForegroundColor Yellow
rustup target add i686-pc-windows-msvc

$crateDir = Join-Path $PSScriptRoot "mbrc-core"
$target = "i686-pc-windows-msvc"

# Build
Write-Host "`nBuilding mbrc_core ($Configuration)..." -ForegroundColor Yellow

$cargoArgs = @("build", "--manifest-path", "$crateDir\Cargo.toml", "--target", $target)
if ($Configuration -eq "release") {
    $cargoArgs += "--release"
}

& cargo @cargoArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Cargo build failed"
    exit $LASTEXITCODE
}

# Determine source path
$profile = if ($Configuration -eq "release") { "release" } else { "debug" }
$sourceDll = Join-Path $crateDir "target\$target\$profile\mbrc_core.dll"

if (-not (Test-Path $sourceDll)) {
    Write-Error "Built DLL not found at: $sourceDll"
    exit 1
}

$size = (Get-Item $sourceDll).Length / 1KB
Write-Host "`nBuilt mbrc_core.dll ($([math]::Round($size, 1)) KB)" -ForegroundColor Green

# Copy to build output directories (SDK-style projects output to net48 subdirectory)
$copyTargets = @(
    (Join-Path $PSScriptRoot "build\bin\plugin\Debug\net48"),
    (Join-Path $PSScriptRoot "build\bin\plugin\Release\net48")
)

foreach ($dest in $copyTargets) {
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item $sourceDll -Destination $dest -Force
    Write-Host "Copied to $dest" -ForegroundColor Cyan
}

# Copy to MusicBee Plugins folder
$musicBeePlugins = Join-Path $PSScriptRoot "app\MusicBee\Plugins"
if (Test-Path $musicBeePlugins) {
    Copy-Item $sourceDll -Destination $musicBeePlugins -Force
    Write-Host "Copied to $musicBeePlugins" -ForegroundColor Cyan
}

Write-Host "`nRust build complete!" -ForegroundColor Green
