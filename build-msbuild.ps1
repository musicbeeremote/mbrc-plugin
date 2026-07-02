# Build script using MSBuild directly (similar to build.bat)
param(
    [string]$Configuration = "Release"
)

# Find MSBuild. Prefer vswhere (version-independent: resolves VS 2026, 2022, any
# edition, incl. prerelease/Insiders); fall back to probing well-known locations.
$msbuild = $null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -prerelease -products * `
        -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    # Fallback: VS 2026 installs under "18", VS 2022 under "2022".
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

if (-not $msbuild) {
    Write-Error "MSBuild not found. Please install Visual Studio 2026 (or 2022) with the MSBuild component."
    exit 1
}

Write-Host "Using MSBuild: $msbuild" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Green

# Clean previous output
if (Test-Path ".\build") {
    Write-Host "Cleaning previous build output..." -ForegroundColor Yellow
    Remove-Item -Path ".\build" -Recurse -Force
}

# Restore packages
Write-Host "`nRestoring NuGet packages..." -ForegroundColor Yellow
& $msbuild MBRC.sln /t:Restore /p:Configuration="$Configuration" /v:M

if ($LASTEXITCODE -ne 0) {
    Write-Error "Package restore failed"
    exit $LASTEXITCODE
}

# Build solution
Write-Host "`nBuilding solution..." -ForegroundColor Yellow
& $msbuild MBRC.sln /p:Configuration="$Configuration" /p:Platform="Any CPU" /m /v:M /fl /flp:LogFile=msbuild.log`;Verbosity=Normal /nr:false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed. Check msbuild.log for details."
    exit $LASTEXITCODE
}

Write-Host "`nBuild completed successfully!" -ForegroundColor Green