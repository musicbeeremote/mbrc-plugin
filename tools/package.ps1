<#
.SYNOPSIS
    Builds the release artifacts: installer, zip, checksums and manifest.

.DESCRIPTION
    The same script CI runs, so a local package and a published one are built by
    one implementation rather than two that agree until they do not.

    The manifest it writes is unsigned. Signing needs the minisign secret that
    only CI holds, and the updater refuses an unsigned manifest, so a local
    package tests the installer and the payload but never the update path.

.PARAMETER Version
    Full version string, e.g. 1.6.0 or 1.6.0-local. Stamped into mb_remote.dll,
    the installer and the manifest.

.PARAMETER SkipBuild
    Package whatever is already in build\dist instead of building and staging.
    CI uses this: it downloads the artifacts it tested rather than rebuilding
    them, so the bytes that get hashed are the bytes that were tested.

.EXAMPLE
    .\tools\package.ps1 -Version 1.6.0-local
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet('stable', 'testing')]
    [string]$Channel = 'stable',

    [string]$NotesUrl = '',

    [switch]$SkipBuild,

    [string]$Makensis = "${env:ProgramFiles(x86)}\NSIS\makensis.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'build\dist'
$base = "musicbee_remote_$Version"

function Write-Step($message) {
    Write-Host "`n==> $message" -ForegroundColor Cyan
}

# ------------------------------------------------------------------- build ---
if (-not $SkipBuild) {
    Write-Step "Building both halves (Release, $Version)"
    # The C# version comes from the environment, not /p:Version: a later target
    # that rebuilds the project without the switch would restamp the DLL with
    # the fallback version and ship a plugin that tells clients the wrong one.
    $env:MBRC_VERSION = $Version
    & (Join-Path $root 'build.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'the build failed' }

    Write-Step 'Staging build\dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    Copy-Item "$root\build\bin\plugin\Release\net48\mb_remote.dll" $dist -Force
    Copy-Item "$root\target\i686-pc-windows-msvc\plugin\mbrc_core.dll" $dist -Force
    Copy-Item "$root\target\i686-pc-windows-msvc\plugin\mbrc-helper.exe" $dist -Force
    # Prefixed because the zip is extracted straight into MusicBee's Plugins
    # folder, where a plain README.txt lands on top of MusicBee's own.
    Copy-Item "$root\LICENSE" "$dist\MBRC_LICENSE.txt" -Force
    Copy-Item "$root\installer\README.txt" "$dist\MBRC_README.txt" -Force
}

if (-not (Test-Path "$dist\mb_remote.dll")) {
    throw "build\dist holds no plugin. Run without -SkipBuild to build one."
}

# The stamp is checked rather than trusted: a build that fell back to the
# version in Directory.Build.props looks identical until a client reads it.
$stamped = (Get-Item "$dist\mb_remote.dll").VersionInfo.ProductVersion.Split('+')[0]
if ($stamped -ne $Version) {
    throw "mb_remote.dll is stamped '$stamped' but this package is '$Version'."
}

# --------------------------------------------------------------- installer ---
Write-Step 'Building the installer'
if (-not (Test-Path $Makensis)) {
    throw "makensis.exe not found at $Makensis. Install NSIS or pass -Makensis."
}
$env:PLUGIN_VERSION = $Version
& $Makensis "$root\installer\MusicBeeRemote.nsi"
if ($LASTEXITCODE -ne 0) { throw 'makensis failed' }
Move-Item "$root\installer\$base.exe" $dist -Force

Write-Step 'Creating the zip'
$bundled = @('mb_remote.dll', 'mbrc_core.dll', 'mbrc-helper.exe')
$files = ($bundled + @('MBRC_LICENSE.txt', 'MBRC_README.txt')) |
    ForEach-Object { Join-Path $dist $_ }
Compress-Archive -Path $files -DestinationPath "$dist\$base.zip" -Force

Write-Step 'Writing checksums'
foreach ($ext in @('exe', 'zip')) {
    $name = "$base.$ext"
    $hash = (Get-FileHash -Algorithm SHA512 "$dist\$name").Hash.ToLower()
    "$hash  $name" | Out-File -Encoding ascii "$dist\$name.sha512"
}

# ---------------------------------------------------------------- manifest ---
Write-Step 'Writing the manifest'

# Both constants are read from the sources that already own them, so the
# manifest cannot drift from what the plugin enforces.
$abiSrc = Get-Content "$root\packages\mbrc-core\src\ffi\types.rs" -Raw
if ($abiSrc -notmatch 'MBRC_ABI_VERSION:\s*i32\s*=\s*(\d+)') {
    throw 'MBRC_ABI_VERSION not found in packages/mbrc-core/src/ffi/types.rs'
}
$abiVersion = [int]$Matches[1]

$nsiSrc = Get-Content "$root\installer\MusicBeeRemote.nsi" -Raw
if ($nsiSrc -notmatch '\$R4\s*<\s*(\d+)') {
    throw 'minimum MusicBee build not found in installer/MusicBeeRemote.nsi'
}
$minBuild = [int]$Matches[1]

function Get-Artifact($path, $name) {
    [ordered]@{
        name   = $name
        size   = (Get-Item $path).Length
        sha512 = (Get-FileHash -Algorithm SHA512 $path).Hash.ToLower()
    }
}

# Exactly the files the updater installs: the licence and readme ride along in
# the zip but are not applied, and the extractor writes only what is listed.
$applied = foreach ($name in $bundled) {
    [ordered]@{
        path   = $name
        sha512 = (Get-FileHash -Algorithm SHA512 "$dist\$name").Hash.ToLower()
    }
}

if (-not $NotesUrl) {
    $repo = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'musicbeeremote/mbrc-plugin' }
    $NotesUrl = "https://github.com/$repo/releases/tag/v$Version"
}

$manifest = [ordered]@{
    schema             = 1
    channel            = $Channel
    version            = $Version
    released_at        = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    abi_version        = $abiVersion
    min_musicbee_build = $minBuild
    notes_url          = $NotesUrl
    artifacts          = [ordered]@{
        zip       = Get-Artifact "$dist\$base.zip" "$base.zip"
        installer = Get-Artifact "$dist\$base.exe" "$base.exe"
    }
    files              = @($applied)
}

# No BOM: the signature covers these bytes exactly, and the plugin's
# minisign-verify sees whatever is written here.
$json = ($manifest | ConvertTo-Json -Depth 6) + "`n"
[System.IO.File]::WriteAllText(
    (Join-Path $dist 'manifest.json'),
    $json,
    (New-Object System.Text.UTF8Encoding $false)
)

Write-Host "`nPackaged $Version" -ForegroundColor Green
Get-ChildItem $dist | Select-Object Name, Length | Format-Table -AutoSize
Write-Host 'The manifest is unsigned, so the updater will refuse it: this tests the installer and the payload, not the update path.' -ForegroundColor Yellow
