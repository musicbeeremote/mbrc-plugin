# Stage a locally built, locally signed update - no GitHub release, no network.
#
# WHY THIS EXISTS
#
# The only part of the update mechanism that cannot be tested from a working
# tree is the apply: `elevate::launch` runs the *staged* helper and verifies it
# against a signed manifest, so a locally built binary is rejected. Testing a
# change to the launch therefore meant publishing a release and cutting another
# beta - a slow loop for something that fails in seconds.
#
# This stages a bundle by hand instead, reaching the same code path from
# `pending.json` onwards: verification, the elevation decision, the launch, the
# backup, the swap, the relaunch and the staging sweep. Everything except check
# and download, both of which are already known to work.
#
# HOW THE SIGNATURE WORKS
#
# `mbrc-release/build.rs` compiles every `keys/*.pub` into TRUSTED_KEYS. Drop a
# locally generated public key in there and a local build trusts it, with no
# code change. This script generates one on first run if it is missing.
#
# `packages/mbrc-release/keys/dev*.pub` is gitignored, so it cannot be committed
# by accident, and build.rs prints a warning on every build that compiles one in.
#
#   SECURITY. A build carrying a dev key trusts bundles anyone able to write to
#   the staging directory can produce, and the apply is elevated. Never ship a
#   build made with it. CI builds from a clean checkout, so a stray local key
#   cannot reach a release - but nothing stops you installing such a build
#   yourself, so replace it with a real build when you are done testing.
#
# USAGE
#
#   .\tools\stage-local-update.ps1 -Version 1.5.0-beta.9 -Storage "$env:APPDATA\MusicBee\mb_remote"
#
# The version must be NEWER than what is installed, or `is_upgrade` refuses the
# apply. Build and install a lower-stamped build first.

param(
    [Parameter(Mandatory = $true)] [string]$Version,
    [Parameter(Mandatory = $true)] [string]$Storage,
    [string]$KeyDir = "$env:TEMP\mbrc-devkey",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$keysDir = Join-Path $root "packages\mbrc-release\keys"
$pub = Join-Path $KeyDir "dev.pub"
$sec = Join-Path $KeyDir "dev.key"

function Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }

# --- the signing key -------------------------------------------------------

if (-not (Test-Path $sec)) {
    Step "Generating a local dev keypair in $KeyDir"
    New-Item -ItemType Directory -Force -Path $KeyDir | Out-Null
    # -W: passwordless, so signing never blocks on a prompt.
    rsign generate -W -p $pub -s $sec -c "mbrc local test key" | Out-Null
}

$trusted = Join-Path $keysDir "dev.pub"
if (-not (Test-Path $trusted)) {
    Step "Trusting the dev key for local builds (untracked)"
    Copy-Item $pub $trusted -Force
    Write-Host "  copied to packages\mbrc-release\keys\dev.pub (gitignored)" -ForegroundColor Yellow
    Write-Host "  every build that compiles it in says so; never ship one" -ForegroundColor Yellow
}

# --- build -----------------------------------------------------------------

if (-not $SkipBuild) {
    Step "Building stamped $Version"
    $env:MBRC_VERSION = $Version
    & (Join-Path $root "build.ps1") -Configuration Release | Out-Null
}

$out = Join-Path $root "build\bin\plugin\Release\net48"
$files = @("mb_remote.dll", "mbrc_core.dll", "mbrc-helper.exe")
foreach ($f in $files) {
    if (-not (Test-Path (Join-Path $out $f))) { throw "missing build output: $f" }
}

# --- manifest --------------------------------------------------------------

Step "Writing and signing the manifest"

function Sha512Hex($path) {
    (Get-FileHash -Algorithm SHA512 -Path $path).Hash.ToLowerInvariant()
}

$entries = @()
foreach ($f in $files) {
    $entries += [ordered]@{ path = $f; sha512 = (Sha512Hex (Join-Path $out $f)) }
}

# `artifacts` is required by the schema even though a hand-staged bundle is
# never downloaded; the sizes and hashes below are of the staged files.
$zipName = "musicbee_remote_$Version.zip"
$manifest = [ordered]@{
    schema             = 1
    channel            = "testing"
    version            = $Version
    released_at        = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    abi_version        = 1
    min_musicbee_build = 6500
    notes_url          = "https://example.invalid/local-test"
    artifacts          = [ordered]@{
        zip       = [ordered]@{ name = $zipName; size = 1; sha512 = $entries[0].sha512 }
        installer = [ordered]@{ name = "musicbee_remote_$Version.exe"; size = 1; sha512 = $entries[0].sha512 }
    }
    files              = $entries
}

$staged = Join-Path $Storage "updates\$Version"
New-Item -ItemType Directory -Force -Path $staged | Out-Null

$manifestPath = Join-Path $staged "manifest.json"
# No BOM: the bytes are what gets signed and verified.
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))

rsign sign -W -s $sec -x (Join-Path $staged "manifest.json.minisig") `
    -t "MusicBee Remote $Version (local test)" $manifestPath | Out-Null

# --- stage -----------------------------------------------------------------

Step "Staging into $staged"
foreach ($f in $files) {
    Copy-Item (Join-Path $out $f) (Join-Path $staged $f) -Force
    Write-Host ("  {0}" -f $f)
}

# The marker is what tells the core something is pending.
# `files` is REQUIRED by mbrc_release::stage::Pending. Without it the marker
# fails to deserialize, the core logs "the staged-update marker is unreadable"
# and silently ignores the whole bundle - the panel then just offers a check.
$pending = [ordered]@{
    schema    = 1
    version   = $Version
    staged_at = $manifest.released_at
    files     = @($files)
}
[IO.File]::WriteAllText(
    (Join-Path $Storage "updates\pending.json"),
    ($pending | ConvertTo-Json -Depth 4),
    [Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "Staged $Version." -ForegroundColor Green
Write-Host "Restart MusicBee; the Updates panel should offer 'restart to install'."
Write-Host "The INSTALLED build must be older than $Version and must also trust the dev key."
