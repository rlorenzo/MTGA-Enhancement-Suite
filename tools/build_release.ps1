# Builds a complete release into the release/ directory:
#   - Plugin DLL          (MTGAEnhancementSuite.dll)
#   - Bootstrapper DLL    (already present, copied if newer)
#   - Installer EXE       (MTGAPlus-Installer.exe — always in every release
#                          so new users have a download path)
#   - Signed manifest     (manifest.json, signed with signing_key.pem)
#
# Usage:
#   tools\build_release.ps1 0.15.0
#
# Then upload everything in release/ to the GitHub release:
#   gh release create v0.15.0 release\* --title "..." --notes "..."

param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $repoRoot

$releaseDir = Join-Path $repoRoot "release"
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}

Write-Host "=== Building plugin DLL ==="
dotnet build Plugin\MTGAEnhancementSuite.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed" }
Copy-Item Plugin\bin\Release\MTGAEnhancementSuite.dll $releaseDir\MTGAEnhancementSuite.dll -Force
Write-Host ""

Write-Host "=== Building bootstrapper DLL ==="
dotnet build Bootstrapper\Bootstrapper.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Bootstrapper build failed" }
Copy-Item Bootstrapper\bin\Release\MTGAESBootstrapper.dll $releaseDir\MTGAESBootstrapper.dll -Force
Write-Host ""

Write-Host "=== Publishing installer EXE ==="
# self-contained single-file so users don't need .NET 6 installed
dotnet publish Installer\MTGAESInstaller.csproj -c Release -o Installer\publish --self-contained true
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }
Copy-Item Installer\publish\MTGAPlus-Installer.exe $releaseDir\MTGAPlus-Installer.exe -Force
Write-Host ""

Write-Host "=== Bundling icons ==="
# Icon source PNGs live in assets\icons\ (tracked in git). We stage them
# into release\icons\ for local-deploy parity with what users see, then
# zip into release\icons.zip for distribution. They're NOT in the
# auto-update manifest — icons rarely change and the auto-updater only
# swaps DLLs anyway — but the fresh-install path needs them.
$iconsSrc = Join-Path $repoRoot "assets\icons"
$iconsStage = Join-Path $releaseDir "icons"
$iconsZip = Join-Path $releaseDir "icons.zip"
if (Test-Path $iconsZip)   { Remove-Item $iconsZip -Force }
if (Test-Path $iconsStage) { Remove-Item $iconsStage -Recurse -Force }
if (Test-Path $iconsSrc) {
    New-Item -ItemType Directory -Path $iconsStage -Force | Out-Null
    Copy-Item (Join-Path $iconsSrc "*.png") $iconsStage -Force
    Compress-Archive -Path (Join-Path $iconsStage "*.png") -DestinationPath $iconsZip -Force
    Write-Host "  icons.zip created from $iconsSrc ($(((Get-ChildItem $iconsSrc -Filter *.png).Count)) PNG(s))"
} else {
    Write-Host "  No assets\icons folder found; icons.zip will not be in release"
}
Write-Host ""

Write-Host "=== Signing manifest ==="
# manifest covers DLLs + config only — the auto-updater swaps DLLs at runtime,
# so the installer EXE is intentionally NOT in the manifest. It's just a GitHub
# release asset for first-time-install downloads. Same applies to icons.zip.
python sign_release.py $Version
if ($LASTEXITCODE -ne 0) { throw "sign_release.py failed" }
Write-Host ""

Write-Host "=== Release contents ==="
Get-ChildItem $releaseDir | Format-Table Name, Length, LastWriteTime
Write-Host ""

Write-Host "Release v$Version is ready. Upload with:"
Write-Host "  gh release create v$Version release\* --title `"v$Version - <title>`" --notes `"<notes>`""
Write-Host "or, if the release already exists:"
Write-Host "  gh release upload v$Version release\MTGAPlus-Installer.exe"
