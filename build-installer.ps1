<#
.SYNOPSIS
    Builds and packages DeskSpaceOS for distribution using Velopack.

.DESCRIPTION
    Publishes DeskSpaceOS.Service as a self-contained win-x64 binary, then
    runs 'vpk pack' to produce a Setup.exe installer.

    Prerequisites:
        dotnet tool install -g vpk

.PARAMETER Version
    Semantic version for this release, e.g. "1.2.0".

.PARAMETER Channel
    Velopack release channel — "stable", "beta", or "nightly". Default: "stable".

.PARAMETER LocalReleasesPath
    Drop packages into a local folder instead of uploading to GitHub.
    The installed app will pick up updates from this folder automatically
    when appsettings.json "Updates:Url" points to the same path.
    Example: -LocalReleasesPath "C:\DeskSpaceOS-releases"

.PARAMETER GitHubToken
    Optional GitHub token used to upload release assets.
    Falls back to the GITHUB_TOKEN environment variable.
    Ignored when -LocalReleasesPath is set.

.EXAMPLE
    # Pre-release / local testing
    .\build-installer.ps1 -Version 0.1.0 -LocalReleasesPath C:\DeskSpaceOS-releases

    # Stable GitHub release
    .\build-installer.ps1 -Version 1.0.0
    .\build-installer.ps1 -Version 1.2.0 -GitHubToken ghp_xxx
#>
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [ValidateSet("stable", "beta", "nightly")]
    [string]$Channel = "stable",

    [string]$LocalReleasesPath = "",

    [string]$GitHubToken = $env:GITHUB_TOKEN
)

$ErrorActionPreference = "Stop"

$PublishDir = "artifacts\publish\win-x64"
$DistDir    = if ($LocalReleasesPath) { $LocalReleasesPath } else { "artifacts\dist" }
$RepoUrl    = "https://github.com/Diddlik/DeskSpaceOS"

Write-Host ""
Write-Host "=== DeskSpaceOS Installer Build ===" -ForegroundColor Cyan
Write-Host "  Version : $Version"
Write-Host "  Channel : $Channel"
Write-Host "  Output  : $DistDir"
Write-Host ""

# ── 1. Verify vpk is available ──────────────────────────────────────────────
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Error @"
'vpk' is not installed. Run:
    dotnet tool install -g vpk
then re-run this script.
"@
}

# ── 2. Clean publish dir (never wipe the local releases folder — it accumulates delta packages) ──
if (Test-Path $PublishDir) {
    Write-Host "--> Cleaning $PublishDir"
    Remove-Item $PublishDir -Recurse -Force
}
if ($DistDir -ne $LocalReleasesPath -and (Test-Path $DistDir)) {
    Write-Host "--> Cleaning $DistDir"
    Remove-Item $DistDir -Recurse -Force
}
if ($LocalReleasesPath -and -not (Test-Path $LocalReleasesPath)) {
    New-Item -ItemType Directory -Path $LocalReleasesPath | Out-Null
}

# ── 3. Publish Service (self-contained, win-x64) ─────────────────────────────
Write-Host "--> Publishing DeskSpaceOS.Service..." -ForegroundColor Cyan
dotnet publish DeskSpaceOS.Service/DeskSpaceOS.Service.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained `
    -p:Platform=x64 `
    -p:PublishSingleFile=false `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# ── 3b. Publish SettingsApp into the SAME folder ─────────────────────────────
# The Service launches DeskSpaceOS.SettingsApp.exe from its own install directory
# (OverlayManager.OpenSettingsApp). Service has no project reference to SettingsApp,
# so it must be published separately into the pack dir or the tray "Open Settings"
# action finds nothing in a packaged install. Self-contained (incl. WindowsAppSDK)
# so the WinUI 3 app runs without a machine-wide Windows App Runtime. Trimming is
# disabled: it breaks WinUI 3 XAML reflection.
Write-Host "--> Publishing DeskSpaceOS.SettingsApp..." -ForegroundColor Cyan
dotnet publish DeskSpaceOS.SettingsApp/DeskSpaceOS.SettingsApp.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained `
    -p:Platform=x64 `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish (SettingsApp) failed (exit $LASTEXITCODE)" }

# ── 4. Pack with Velopack ────────────────────────────────────────────────────
Write-Host "--> Packing with Velopack..." -ForegroundColor Cyan
vpk pack `
    --packId     "DeskSpaceOS" `
    --packVersion $Version `
    --packDir    $PublishDir `
    --mainExe    "DeskSpaceOS.Service.exe" `
    --outputDir  $DistDir `
    --channel    $Channel

if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green
Write-Host "  Installer : $DistDir\DeskSpaceOS-win-Setup.exe"
Write-Host ""

# ── 5. Upload or report ──────────────────────────────────────────────────────
if ($LocalReleasesPath) {
    $fwdPath = $LocalReleasesPath.Replace('\', '/')
    Write-Host "Local release ready. To test updates:" -ForegroundColor Yellow
    Write-Host "  1. Install:   $LocalReleasesPath\DeskSpaceOS-win-Setup.exe"
    Write-Host "  2. appsettings.json -> Updates:Url = file:///$fwdPath"
    Write-Host "  3. Build a higher version to the same folder."
    Write-Host "     The running service will pick it up within 4h."
}
elseif ($GitHubToken) {
    Write-Host "--> Uploading to GitHub Releases ($RepoUrl)..." -ForegroundColor Cyan
    # No --merge: single channel/arch. --merge only combines multiple channels into
    # one release and fails re-uploading an existing release (duplicate releases.json).
    vpk upload github `
        --repoUrl   $RepoUrl `
        --token     $GitHubToken `
        --outputDir $DistDir `
        --channel   $Channel `
        --publish

    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed (exit $LASTEXITCODE)" }

    Write-Host "=== Upload complete ===" -ForegroundColor Green
}
else {
    Write-Host "Tip: pass -LocalReleasesPath for local testing, or -GitHubToken to publish." -ForegroundColor DarkGray
}
